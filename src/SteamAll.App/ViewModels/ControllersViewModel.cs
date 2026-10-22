// =====================================================================================
//  ControllersViewModel.cs
//  La pagina "Controllers": un controller disegnato che mostra quello collegato (i tasti si
//  illuminano quando li premi; tutto grigio se non c'è un controller), se Steam Input è acceso per
//  quel tipo di controller, e il pulsante Auto-set, che prepara i controller di tutti i giochi
//  emulati (da Steam il controller virtuale di Steam, fuori da Steam la tua configurazione).
//  Il controller si legge solo mentre la pagina è aperta, 30 volte al secondo.
// =====================================================================================

using System.Collections.ObjectModel;
using Avalonia.Threading;   // DispatcherTimer: un timer che scatta sul thread dell'interfaccia
using SteamAll.Core.Controllers;
using SteamAll.Core.Games;
using SteamAll.Core.Launchers;
using SteamAll.Core.Plan;
using SteamAll.Core.Settings;
using SteamAll.Services;

namespace SteamAll.ViewModels;

public sealed class ControllersViewModel : PageViewModel
{
    private readonly MainWindowViewModel shell;
    private readonly DispatcherTimer timer;
    private IGamepadReader? reader;
    private GamepadState state = GamepadState.None;

    public override Avalonia.Media.Geometry Icon => Icons.Controllers;

    public ControllersViewModel(MainWindowViewModel shell)
        : base("Controllers", "Plug & play controllers in every game, emulated ones too.")
    {
        this.shell = shell;
        AutoSetCommand = new RelayCommand(AskToAutoSet);
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += (_, _) => Poll();
    }

    public override void OnShown()
    {
        reader ??= Gamepads.Create(shell.Platform.Os);   // "??=" = solo la prima volta
        timer.Start();
    }

    public override void OnHidden() => timer.Stop();

    private void Poll()
    {
        GamepadState next = reader?.Read() ?? GamepadState.None;
        if (next == state) return;   // i record si confrontano per valore: niente di nuovo, niente da ridisegnare
        bool changedConnection = next.Connected != state.Connected || next.Family != state.Family || next.Name != state.Name;
        state = next;
        OnPropertyChanged(string.Empty);   // "" = tutte le proprietà sono cambiate
        if (changedConnection) OnPropertyChanged(nameof(SteamInputText));
    }

    // --- Il controller disegnato (coordinate del disegno, 560 x 360) -------------------------

    public bool Connected => state.Connected;
    public double PadOpacity => state.Connected ? 1 : 0.35;
    public bool South => state.South;
    public bool East => state.East;
    public bool West => state.West;
    public bool North => state.North;
    public bool LeftBumper => state.LeftBumper;
    public bool RightBumper => state.RightBumper;
    public bool Back => state.Back;
    public bool Start => state.Start;
    public bool Guide => state.Guide;
    public bool Up => state.Up;
    public bool Down => state.Down;
    public bool Left => state.Left;
    public bool Right => state.Right;
    public bool LeftStick => state.LeftStick || Math.Abs(state.LeftX) > 0.2 || Math.Abs(state.LeftY) > 0.2;
    public bool RightStick => state.RightStick || Math.Abs(state.RightX) > 0.2 || Math.Abs(state.RightY) > 0.2;
    // Le levette: il cerchio interno si sposta fino a 16 pixel dal centro.
    public double LeftThumbX => 175 - 24 + state.LeftX * 16;
    public double LeftThumbY => 150 - 24 + state.LeftY * 16;
    public double RightThumbX => 355 - 22 + state.RightX * 16;
    public double RightThumbY => 228 - 22 + state.RightY * 16;
    // I grilletti: una barra che si riempie.
    public double LeftTriggerWidth => 96 * Math.Clamp(state.LeftTrigger, 0, 1);
    public double RightTriggerWidth => 96 * Math.Clamp(state.RightTrigger, 0, 1);
    // Sotto i 14 pixel la barra arrotondata si disegna male: la mostriamo solo quando si vede davvero.
    public bool LeftTriggerOn => LeftTriggerWidth >= 14;
    public bool RightTriggerOn => RightTriggerWidth >= 14;

    // --- I testi ------------------------------------------------------------------------------

    public string ControllerTitle => state.Connected ? state.Name : "No controller connected";
    public string ControllerHint => state.Connected ? "Press any button or move the sticks to try it." : "Connect a controller to try it.";

    private static string FamilyName(ControllerFamily f) => f switch
    {
        ControllerFamily.Xbox => "Xbox",
        ControllerFamily.PlayStation => "PlayStation",
        ControllerFamily.Nintendo => "Nintendo",
        _ => "generic",
    };

    /// <summary>Steam Input è acceso per questo tipo di controller? (da localconfig.vdf dell'utente di Steam)</summary>
    public string SteamInputText
    {
        get
        {
            if (!state.Connected) return "";
            string? config = shell.Plan?.Library?.User.ConfigFolder;
            bool? on = config is null ? null : SteamInputSettings.IsEnabledFor(config, state.Family);
            string family = FamilyName(state.Family);
            return on switch
            {
                true => $"Steam Input is on for {family} controllers: games started from Steam get it automatically.",
                false => $"Steam Input is off for {family} controllers: turn it on in Steam > Settings > Controller.",
                null => $"Steam Input uses Steam's default for {family} controllers (Steam > Settings > Controller).",
            };
        }
    }

    // --- Auto-set e avvisi ------------------------------------------------------------------------

    public RelayCommand AutoSetCommand { get; }
    public ObservableCollection<string> LayoutWarnings { get; } = [];
    public bool HasLayoutWarnings => LayoutWarnings.Count > 0;

    public void SetPlan(ImportPlan plan)
    {
        LayoutWarnings.Clear();
        string? steam = shell.Platform.FindSteamFolder();
        if (steam is not null && plan.Library is not null)
        {
            IEnumerable<string> names = plan.Entries.Where(e => e.Shortcut is not null).Select(e => e.Game.Name);
            foreach (LayoutWarning w in SteamInputLayouts.Check(steam, plan.Library.User.Id, names))
                LayoutWarnings.Add($"{w.Game} ({w.Controller}): {w.Problem}");
        }
        OnPropertyChanged(nameof(HasLayoutWarnings));
        OnPropertyChanged(nameof(SteamInputText));
    }

    private static bool IsIncluded(PlanEntry e) => e.Problem is null && e.Status is SteamStatus.New or SteamStatus.InSteam;

    // Prepara launcher e configurazioni del controller di tutti i giochi emulati (Steam non viene toccato).
    private void AskToAutoSet()
    {
        if (shell.Plan is not { } plan) return;
        List<PlanEntry> entries = plan.Entries.Where(e => e.Game.Origin == GameOrigin.Folder && e.Game.Emulator is not null && IsIncluded(e)).ToList();
        var ids = plan.Entries.Where(e => e.Game.Origin == GameOrigin.Emulator && IsIncluded(e)).Select(e => e.Game.EmulatorSetupId).ToHashSet();
        List<EmulatorSetup> setups = shell.Settings.Emulators.Where(s => ids.Contains(s.Id)).ToList();
        var os = shell.Platform.Os;
        shell.Dialog.Ask("Auto-set controllers",
            $"SteamAll prepares the controller of {entries.Count + setups.Count} emulators: from Steam they get Steam's virtual " +
            "controller, so any pad works; outside Steam they keep your own setup. Steam is not touched. Close any running game first.",
            "Auto-set", async () =>
            {
                shell.Settings.ManageControllers = true;
                shell.SaveSettings();
                foreach (PlanEntry e in entries)
                {
                    // Task.Run: il lavoro sui file va in background; la riga si aggiunge sul thread dell'interfaccia.
                    LauncherResult r = await Task.Run(() => LauncherInstaller.Install(e.Game, true, os));
                    shell.Dialog.AddLine($"{e.Game.Name}: {r.Message}");
                }
                foreach (EmulatorSetup s in setups)
                {
                    LauncherResult r = await Task.Run(() => LauncherInstaller.InstallSetup(s, true, os));
                    shell.Dialog.AddLine($"{s.Emulator?.Name ?? s.EmulatorId}: {r.Message}");
                }
                shell.Dialog.Message = "Done: your controllers are set for every emulated game, from their next start.";
            });
    }
}
