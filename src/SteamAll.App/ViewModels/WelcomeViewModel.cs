// =====================================================================================
//  WelcomeViewModel.cs
//  La finestra di benvenuto del primo avvio (e di Settings > "What SteamAll adds"): come vuoi
//  usare SteamAll?
//    - "Installed games only": come BoilR, nient'altro da installare;
//    - "Your whole library": anche i giochi posseduti e non installati, con il tasto Installa. Per
//      quello Steam ha bisogno di Millennium e del plugin di SteamAll: il secondo passo li prepara
//      (Millennium si scarica dal suo sito, il plugin lo installa SteamAll, che lo porta con sé).
// =====================================================================================

using SteamAll.Core.Settings;

namespace SteamAll.ViewModels;

public sealed class WelcomeViewModel : ViewModelBase
{
    private readonly MainWindowViewModel shell;
    private bool isOpen, isSettingUp;

    public WelcomeViewModel(MainWindowViewModel shell)
    {
        this.shell = shell;
        ChooseInstalledCommand = new RelayCommand(() =>
        {
            shell.SetMode(AppSettings.ModeInstalled);
            IsOpen = false;
        });
        ChooseAllCommand = new RelayCommand(() =>
        {
            shell.SetMode(AppSettings.ModeAll);
            IsSettingUp = true;   // secondo passo: Millennium e plugin
        });
        CloseCommand = new RelayCommand(() => IsOpen = false);
    }

    public bool IsOpen { get => isOpen; private set => SetField(ref isOpen, value); }

    /// <summary>true nel secondo passo (Millennium e plugin), false nella scelta.</summary>
    public bool IsSettingUp
    {
        get => isSettingUp;
        private set
        {
            if (SetField(ref isSettingUp, value)) OnPropertyChanged(nameof(IsChoosing));
        }
    }

    public bool IsChoosing => !isSettingUp;

    public RelayCommand ChooseInstalledCommand { get; }
    public RelayCommand ChooseAllCommand { get; }
    public RelayCommand CloseCommand { get; }

    /// <summary>Apre la scelta (primo avvio).</summary>
    public void OpenChoice()
    {
        IsSettingUp = false;
        IsOpen = true;
    }

    /// <summary>Apre direttamente il secondo passo (da Settings, quando scegli "Your whole library").</summary>
    public void OpenSetup()
    {
        shell.RefreshMillennium();
        IsSettingUp = true;
        IsOpen = true;
    }
}
