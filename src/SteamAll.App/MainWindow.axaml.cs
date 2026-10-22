// =====================================================================================
//  MainWindow.axaml.cs
//  La metà in C# della finestra principale: come si apre e come si ricorda.
//    - La prima volta si apre a schermo intero (ingrandita). Poi come l'hai chiusa l'ultima volta:
//      ingrandita, oppure a finestra con la sua misura.
//    - A finestra si adatta comunque allo schermo, così non esce mai dai bordi (sui portatili con lo
//      schermo ingrandito al 200% 1280x800 non ci stanno, e la barra del titolo finiva fuori).
// =====================================================================================

using System.Runtime.InteropServices;   // DllImport: per chiamare una funzione di Windows
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;   // Screen
using SteamAll.ViewModels;

namespace SteamAll;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => OnOpened();
        Closing += (_, _) => RememberWindow();
    }

    // Opened: la finestra è comparsa. Si sistema la misura e solo allora si leggono i giochi, così la
    // finestra si apre subito invece di restare invisibile durante la lettura.
    private void OnOpened()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        DarkTitleBar();
        RestoreWindow(vm);
        // La prima lettura di Steam e delle fonti. "_ =" = avvia il lavoro senza aspettarlo qui.
        // Attenzione: senza questa riga la libreria resta vuota finché non premi Refresh (al primo
        // avvio non si nota, perché la scelta nel benvenuto fa partire comunque una lettura).
        _ = vm.RefreshAsync();
    }

    private void RestoreWindow(MainWindowViewModel vm)
    {
        Width = vm.Settings.WindowWidth;
        Height = vm.Settings.WindowHeight;
        FitToScreen();
        // Prima la misura a finestra (serve quando la riduci), poi lo schermo intero se l'ultima volta era così.
        if (vm.Settings.WindowMaximized) WindowState = WindowState.Maximized;
    }

    // Chiudendo: ingrandita o no, e la misura a finestra (solo se era a finestra: da ingrandita non è quella vera).
    private void RememberWindow()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.Settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            vm.Settings.WindowWidth = Width;
            vm.Settings.WindowHeight = Height;
        }
        vm.SaveSettings();
    }

    private void FitToScreen()
    {
        if (Screens.ScreenFromWindow(this) is not Screen screen) return;
        // WorkingArea = lo schermo senza la barra delle applicazioni, in pixel veri; Scaling = l'ingrandimento
        // di Windows (2 = 200%). Width e Height della finestra sono in pixel "logici" (prima dell'ingrandimento).
        PixelRect area = screen.WorkingArea;
        double scale = screen.Scaling;
        const double frame = 48;   // la barra del titolo e i bordi, che stanno fuori dall'area del contenuto
        Width = Math.Min(Width, area.Width / scale - 24);
        Height = Math.Min(Height, area.Height / scale - frame);
        int w = (int)(Width * scale), h = (int)((Height + frame) * scale);
        Position = new PixelPoint(area.X + Math.Max(0, (area.Width - w) / 2), area.Y + Math.Max(0, (area.Height - h) / 2));
    }

    // La barra del titolo: Windows 11 la colora con il colore d'accento di Windows (se nelle sue
    // impostazioni è acceso), che stonerebbe con il rosso dell'app. La chiediamo scura come il resto.
    // DwmSetWindowAttribute con DWMWA_CAPTION_COLOR (35) c'è solo da Windows 11 (build 22000).
    private void DarkTitleBar()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || TryGetPlatformHandle() is not { } handle) return;
        int color = 0x1C1714;   // #14171C, il colore della barra in basso, scritto al contrario (0x00BBGGRR)
        _ = DwmSetWindowAttribute(handle.Handle, 35, ref color, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}