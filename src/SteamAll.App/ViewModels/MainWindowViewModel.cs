// =====================================================================================
//  MainWindowViewModel.cs
//  Il "cervello" della finestra principale: tiene le impostazioni e il piano, le pagine del menu,
//  i comandi Refresh e Apply, la finestra di benvenuto, quella di conferma (Dialog), e lo stato di
//  Millennium e del plugin del tasto Installa (servono sia al benvenuto sia a Settings).
//  Le pagine gli chiedono di salvare le scelte e di rileggere tutto quando cambia qualcosa.
// =====================================================================================

using System.Diagnostics;   // Process: per aprire un sito nel browser
using SteamAll.Core.Apply;
using SteamAll.Core.Millennium;
using SteamAll.Core.Plan;
using SteamAll.Core.Platform;
using SteamAll.Core.Settings;
using SteamAll.Core.Steam;

namespace SteamAll.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    // Un solo HttpClient per tutto il programma (per SteamGridDB e le immagini degli store).
    internal static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public const string MillenniumWebsite = "https://steambrew.app";

    private readonly string settingsPath;
    private ImportPlan? plan;
    private PageViewModel currentPage;
    private bool isBusy;
    private int batchDepth;      // > 0 mentre selezioni/deselezioni tanti giochi insieme
    private bool savePending;    // c'è da salvare alla fine del "batch"
    private bool refreshPending; // qualcosa è cambiato mentre si leggeva: si rilegge alla fine
    private PluginState pluginState = PluginState.MillenniumMissing;
    private GameDetailViewModel? detail;

    public MainWindowViewModel(AppSettings settings, string settingsPath, IPlatform platform)
    {
        Settings = settings;
        Platform = platform;
        this.settingsPath = settingsPath;

        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !IsBusy);
        ApplyCommand = new RelayCommand(AskToApply, () => !IsBusy && plan?.Library is not null);
        OpenMillenniumCommand = new RelayCommand(() => OpenLink(MillenniumWebsite));
        InstallPluginCommand = new RelayCommand(AskToInstallPlugin, () => pluginState is not (PluginState.Installed or PluginState.MillenniumMissing));
        RemovePluginCommand = new RelayCommand(AskToRemovePlugin, () => pluginState is not (PluginState.NotInstalled or PluginState.MillenniumMissing));
        Welcome = new WelcomeViewModel(this);

        Library = new LibraryViewModel(this);
        Stores = new StoresViewModel(this);
        Sources = new SourcesViewModel(this);
        Controllers = new ControllersViewModel(this);
        Recommended = new RecommendedViewModel(this);
        SettingsPage = new SettingsViewModel(this);
        Pages = [Library, Stores, Sources, Controllers, Recommended, SettingsPage];
        currentPage = Library;
        Sources.SetEmulators(null);
        RefreshMillennium();

        if (settings.Mode.Length == 0) Welcome.OpenChoice();   // primo avvio: come vuoi usare SteamAll?
    }

    public AppSettings Settings { get; }
    public IPlatform Platform { get; }
    public ImportPlan? Plan => plan;
    public DialogViewModel Dialog { get; } = new();
    public WelcomeViewModel Welcome { get; }

    public LibraryViewModel Library { get; }
    public StoresViewModel Stores { get; }
    public SourcesViewModel Sources { get; }
    public ControllersViewModel Controllers { get; }
    public RecommendedViewModel Recommended { get; }
    public SettingsViewModel SettingsPage { get; }

    /// <summary>Le voci del menu a sinistra.</summary>
    public IReadOnlyList<PageViewModel> Pages { get; }

    /// <summary>La pagina mostrata: la View sceglie da sola il suo aspetto (vedi DataTemplates in MainWindow.axaml).</summary>
    public PageViewModel CurrentPage
    {
        get => currentPage;
        set
        {
            PageViewModel old = currentPage;
            if (!SetField(ref currentPage, value)) return;
            old.OnHidden();     // per esempio la pagina Controllers smette di leggere il controller
            value.OnShown();
            CloseGame();        // cambiando pagina, la pagina di dettaglio si chiude
        }
    }

    /// <summary>La pagina di dettaglio aperta (sopra la libreria), o null.</summary>
    public GameDetailViewModel? Detail
    {
        get => detail;
        private set { if (SetField(ref detail, value)) OnPropertyChanged(nameof(HasDetail)); }
    }

    public bool HasDetail => detail is not null;

    /// <summary>Un clic sulla copertina di un gioco: la sua pagina di dettaglio.</summary>
    public void OpenGame(GameTileViewModel tile) => Detail = new GameDetailViewModel(this, tile);

    public void CloseGame() => Detail = null;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetField(ref isBusy, value)) return;
            RefreshCommand.RaiseCanExecuteChanged();
            ApplyCommand.RaiseCanExecuteChanged();
        }
    }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand ApplyCommand { get; }

    /// <summary>Dove si ricordano gli hero scritti da SteamAll (accanto alle impostazioni).</summary>
    private string ArtworkStatePath => Path.Combine(Path.GetDirectoryName(settingsPath)!, "artwork.json");

    /// <summary>Apre un sito nel browser predefinito (UseShellExecute = "come un doppio clic").</summary>
    public static void OpenLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { /* nessun browser: niente da fare */ }
    }

    // ---------------------------------------------------------------------------------
    //  Lettura
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Rilegge Steam e tutte le fonti e ricostruisce il piano. "async Task": mentre lavora in
    /// background (Task.Run) la finestra resta usabile; dopo "await" si torna sul thread
    /// dell'interfaccia, l'unico che può toccare quello che si vede.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            refreshPending = true;   // finita la lettura in corso, ne parte un'altra con le scelte nuove
            return;
        }
        IsBusy = true;
        refreshPending = false;
        try
        {
            // Una "tupla": due risultati insieme, il piano e gli utenti di Steam.
            (ImportPlan newPlan, List<SteamUser> users) = await Task.Run(() =>
            {
                ImportPlan p = PlanBuilder.Build(Settings, Platform);
                string? steam = Platform.FindSteamFolder();
                return (p, steam is null ? new List<SteamUser>() : new SteamInstall(steam).GetUsers());
            });
            plan = newPlan;
            CloseGame();   // il dettaglio aperto era di un gioco del piano vecchio
            Library.SetPlan(plan);
            Stores.SetPlan(plan);
            Sources.SetPlan(plan);
            Controllers.SetPlan(plan);
            Recommended.Refresh();
            SettingsPage.SetUsers(users);
            RefreshMillennium();
        }
        catch (Exception ex)
        {
            Dialog.Show("Something went wrong", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
        if (refreshPending) await RefreshAsync();
    }

    // ---------------------------------------------------------------------------------
    //  Apply to Steam
    // ---------------------------------------------------------------------------------

    private void AskToApply()
    {
        if (plan is null) return;
        ImportPlan current = plan;   // il piano di adesso: se nel frattempo si rilegge, lavoriamo su questo
        var applier = new SteamApplier(Platform, Http);
        ApplySummary s = applier.Summarize(current, Settings);

        string steamLine = s.SteamWillRestart
            ? "Steam will close now and open again by itself when SteamAll is done."
            : "Steam is not running: it stays closed.";
        string message =
            $"{Plural(s.Add, "game")} to add, {s.Update} to update, {s.Remove} to remove, {Plural(s.Launchers, "game launcher")} to write.\n" +
            "Missing artwork and the images you chose are downloaded too. A backup of your Steam library is kept (shortcuts.vdf.steamall-backup).\n\n" +
            steamLine;

        Dialog.Ask("Apply to Steam", message, "Apply", async () =>
        {
            // Progress<T>: le righe dell'avanzamento arrivano dal lavoro in background e vengono
            // mostrate sul thread dell'interfaccia (Progress si occupa del passaggio).
            var progress = new Progress<string>(Dialog.AddLine);
            ApplyReport report = await Task.Run(() => applier.ApplyAsync(current, Settings, ArtworkStatePath, progress));
            Dialog.Message =
                $"Done: {report.Added} added, {report.Updated} updated, {report.Removed} removed, " +
                $"{report.LaunchersWritten} launchers, {report.ImagesWritten} images" +
                (report.CleanedFiles > 0 ? $", {report.CleanedFiles} leftover images moved to the Recycle Bin" : "") + "." +
                (report.Warnings.Count > 0 ? $"\n{report.Warnings.Count} warning(s): see the list below." : "") +
                (report.SteamRestarted ? "\nSteam is starting again." : "");
            await RefreshAsync();
        });
    }

    // "1 game", "2 games": il plurale inglese giusto.
    private static string Plural(int count, string word) => $"{count} {word}{(count == 1 ? "" : "s")}";

    // ---------------------------------------------------------------------------------
    //  Millennium e il plugin del tasto Installa
    // ---------------------------------------------------------------------------------

    public bool MillenniumInstalled => pluginState != PluginState.MillenniumMissing;
    public string MillenniumButtonText => MillenniumInstalled ? "Installed" : "Get Millennium";
    public bool PluginInstalled => pluginState == PluginState.Installed;

    public string PluginStatus => pluginState switch
    {
        PluginState.MillenniumMissing => "Needs Millennium first.",
        PluginState.NotInstalled => "Not installed: games you haven't installed show Play (it still opens their store).",
        PluginState.Disabled => "Installed but turned off in Millennium.",
        PluginState.Outdated => "Installed, but an older version.",
        _ => "Installed and on: games you haven't installed show Install.",
    };

    public string PluginButtonText => pluginState switch
    {
        PluginState.Installed => "Installed",
        PluginState.Outdated or PluginState.Disabled => "Update",
        _ => "Install",
    };

    public RelayCommand OpenMillenniumCommand { get; }
    public RelayCommand InstallPluginCommand { get; }
    public RelayCommand RemovePluginCommand { get; }

    /// <summary>Rilegge se Millennium e il plugin ci sono (quando torni dopo averli installati).</summary>
    public void RefreshMillennium()
    {
        string? steam = Platform.FindSteamFolder();
        pluginState = steam is null ? PluginState.MillenniumMissing : PluginInstaller.Status(steam);
        foreach (string name in (string[])[nameof(MillenniumInstalled), nameof(MillenniumButtonText), nameof(PluginInstalled), nameof(PluginStatus), nameof(PluginButtonText)])
            OnPropertyChanged(name);
        InstallPluginCommand.RaiseCanExecuteChanged();
        RemovePluginCommand.RaiseCanExecuteChanged();
        Recommended?.Refresh();   // "?." : durante il costruttore la pagina non c'è ancora
    }

    private void AskToInstallPlugin()
    {
        string? steam = Platform.FindSteamFolder();
        if (steam is null) return;
        Dialog.Ask("Install button in Steam",
            "SteamAll copies its small plugin (it carries it inside) into Millennium's plugins and turns it on " +
            "(a backup of Millennium's config.json is kept). Steam restarts now if it is open, to load it.",
            "Install", async () =>
            {
                await Task.Run(() => PluginInstaller.Install(steam));
                Dialog.AddLine("Plugin copied and turned on");
                await RestartSteamAsync();
                Dialog.Message = "Done. Games you haven't installed now show Install in Steam.";
                RefreshMillennium();
            });
    }

    private void AskToRemovePlugin()
    {
        string? steam = Platform.FindSteamFolder();
        if (steam is null) return;
        Dialog.Ask("Remove the Install button",
            "SteamAll turns its plugin off in Millennium and moves its folder to the Recycle Bin. Steam restarts if it is open.",
            "Remove", async () =>
            {
                await Task.Run(() => PluginInstaller.Uninstall(steam, Platform));
                Dialog.AddLine("Plugin turned off and moved to the Recycle Bin");
                await RestartSteamAsync();
                Dialog.Message = "Done.";
                RefreshMillennium();
            });
    }

    /// <summary>Steam carica plugin e temi all'avvio: se è aperto lo chiudiamo e riapriamo.</summary>
    public async Task RestartSteamAsync()
    {
        if (!Platform.IsSteamRunning()) return;
        Dialog.AddLine("Closing Steam...");
        if (await Platform.ShutdownSteamAsync(TimeSpan.FromSeconds(60)))
        {
            Platform.StartSteam();
            Dialog.AddLine("Steam started again");
        }
        else
        {
            Dialog.AddLine("Steam didn't close: restart it yourself");
        }
    }

    // ---------------------------------------------------------------------------------
    //  Le scelte (le chiamano le pagine)
    // ---------------------------------------------------------------------------------

    /// <summary>Come usi SteamAll: solo i giochi installati, o tutta la libreria (vedi AppSettings.Mode).</summary>
    public void SetMode(string mode)
    {
        if (Settings.Mode == mode) return;
        Settings.Mode = mode;
        SaveSettings();
        SettingsPage.OnModeChanged();
        _ = RefreshAsync();
    }

    /// <summary>Un gioco ha cambiato spunta: la ricordiamo nelle impostazioni.</summary>
    public void OnGameToggled(GameTileViewModel tile)
    {
        string key = tile.Entry.Game.Key;
        if (tile.Entry.IsChecked) Settings.ExcludedGames.Remove(key);
        else Settings.ExcludedGames.Add(key);
        SaveSettings();
    }

    /// <summary>Uno store acceso o spento: si salva e si rilegge (uno store spento non si legge nemmeno).</summary>
    public void SetStoreEnabled(string storeId, bool enabled)
    {
        if (enabled) Settings.DisabledStores.Remove(storeId);
        else Settings.DisabledStores.Add(storeId);
        SaveSettings();
        _ = RefreshAsync();
    }

    /// <summary>Un emulatore installato in più (da completare nella pagina: programma e cartelle).</summary>
    public void AddEmulator()
    {
        Settings.Emulators.Add(new EmulatorSetup { EmulatorId = Core.Games.Emulator.All[0].Id });
        SaveSettings();
        Sources.SetEmulators(plan);
    }

    public void RemoveEmulator(EmulatorSetup setup)
    {
        Settings.Emulators.Remove(setup);
        OnEmulatorsChanged();
    }

    /// <summary>Un emulatore installato è cambiato (programma, cartelle, acceso/spento): si salva e si rilegge.</summary>
    public void OnEmulatorsChanged()
    {
        SaveSettings();
        Sources.SetEmulators(plan);
        _ = RefreshAsync();
    }

    /// <summary>Una cartella dei giochi ha cambiato spunta: vale per tutti i suoi giochi.</summary>
    public void SetSourceEnabled(string source, bool enabled)
    {
        if (enabled) Settings.ExcludedSources.Remove(source);
        else Settings.ExcludedSources.Add(source);
        SaveSettings();
        if (plan is null) return;
        foreach (PlanEntry e in plan.Entries.Where(e => e.Game.Source == source)) e.IsSourceEnabled = enabled;
        Library.RefreshStatuses();
    }

    public void AddFolder(string path)
    {
        // Contains con StringComparer.OrdinalIgnoreCase: c'è già, magari scritta con maiuscole diverse?
        if (Settings.GameFolders.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        Settings.GameFolders.Add(path);
        SaveSettings();
        _ = RefreshAsync();
    }

    public void RemoveFolder(string path)
    {
        Settings.GameFolders.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        SaveSettings();
        _ = RefreshAsync();
    }

    /// <summary>Inizio di tante modifiche insieme (es. "Select all"): si salva una volta sola, alla fine.</summary>
    public void BeginBatch() => batchDepth++;

    public void EndBatch()
    {
        if (--batchDepth > 0) return;   // "--x" = diminuisci di 1, poi usa il valore
        if (savePending) SaveSettings();
    }

    public void SaveSettings()
    {
        if (batchDepth > 0) { savePending = true; return; }
        savePending = false;
        try
        {
            Settings.Save(settingsPath);
        }
        catch (Exception ex)
        {
            Dialog.Show("Can't save the settings", ex.Message);
        }
    }
}
