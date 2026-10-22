// =====================================================================================
//  PlanBuilder.cs
//  Legge tutto quello che serve al piano (Steam, le cartelle dei giochi, gli emulatori installati,
//  gli store accesi) e lo passa a ImportPlan.Create. Qui stanno le letture "vere" su disco; il
//  confronto sta in ImportPlan. Solo lettura: non scrive niente.
// =====================================================================================

using SteamAll.Core.Games;
using SteamAll.Core.Launchers;
using SteamAll.Core.Platform;
using SteamAll.Core.Settings;
using SteamAll.Core.Sources;
using SteamAll.Core.Steam;
using SteamAll.Core.Stores;

namespace SteamAll.Core.Plan;

public static class PlanBuilder
{
    /// <summary>SteamAll stesso: su Linux e macOS è il launcher dei giochi emulati ("--launch").</summary>
    public static string SelfExe => Environment.ProcessPath ?? "";

    /// <summary>Il programma che Steam avvia per i giochi di un emulatore installato.</summary>
    public static string LauncherFor(EmulatorSetup setup, OsKind os) =>
        os == OsKind.Windows ? LauncherInstaller.SharedLauncherExe(setup.Id) : SelfExe;

    /// <summary>
    /// Legge tutto e costruisce il piano, con le tue scelte già applicate. Può metterci qualche
    /// secondo (gli store): l'interfaccia lo chiama in background.
    /// </summary>
    public static ImportPlan Build(AppSettings settings, IPlatform platform)
    {
        var log = new List<string>();

        // 1. Steam: l'utente e i suoi giochi non Steam.
        ShortcutLibrary? library = null;
        string? steamFolder = platform.FindSteamFolder();
        SteamUser? user = steamFolder is null ? null : new SteamInstall(steamFolder).ChooseUser(settings.SteamUserId);
        if (steamFolder is null) log.Add("Steam: not found on this computer");
        else if (user is null) log.Add($"Steam: no users in {steamFolder}");
        else
        {
            try
            {
                library = ShortcutLibrary.Load(user);
                log.Add($"Steam: account {user}, {library.Shortcuts.Count} non-Steam games");
            }
            catch (Exception ex)
            {
                log.Add($"Steam: can't read the library of {user} ({ex.Message})");
            }
        }

        // 2. Le cartelle dei giochi, una fonte per cartella.
        var sources = new List<SourceResult>();
        foreach (string folder in settings.GameFolders)
        {
            sources.Add(Directory.Exists(folder)
                ? new SourceResult(folder, SourceKind.Folder, Markers.FolderStore, GamesFolderSource.Scan(folder, platform.Os, SelfExe))
                : new SourceResult(folder, SourceKind.Folder, Markers.FolderStore, Games: null, Error: "folder not found"));   // "Nome: valore" = argomento con nome
        }

        // 3. Gli emulatori installati (quelli accesi), con le loro cartelle di ROM.
        var names = new HashSet<string>();
        foreach (EmulatorSetup setup in settings.Emulators.Where(e => e.Enabled))
        {
            string name = setup.Emulator?.Name ?? setup.EmulatorId;
            for (int i = 2; !names.Add(name); i++) name = $"{setup.Emulator?.Name ?? setup.EmulatorId} ({i})";   // due Dolphin: "Dolphin (2)"
            sources.Add(Read(name, SourceKind.Emulator, setup.Id, () =>
            {
                if (!File.Exists(setup.ExePath)) throw new FileNotFoundException($"emulator not found: {setup.ExePath}");
                // Una cartella che non c'è (un disco esterno staccato?) blocca la lettura: così i suoi
                // giochi già in Steam restano, invece di sembrare spariti.
                if (setup.RomFolders.FirstOrDefault(f => !Directory.Exists(f)) is { } missing)
                    throw new DirectoryNotFoundException($"ROM folder not found: {missing}");
                return RomScanner.Scan(setup, name, LauncherFor(setup, platform.Os));
            }));
        }

        // 4. Gli store: quelli installati e accesi si leggono; gli altri si ricordano solo per
        //    togliere da Steam le loro voci.
        var storeStates = new Dictionary<string, StoreStatus>();
        var reports = new List<StoreReport>();
        foreach (IStoreProvider store in StoreCatalog.For(platform))
        {
            StoreStatus status;
            try
            {
                status = !store.IsInstalled() ? StoreStatus.NotInstalled
                       : settings.DisabledStores.Contains(store.Info.Id) ? StoreStatus.Disabled
                       : StoreStatus.Active;
            }
            catch (Exception)
            {
                status = StoreStatus.NotInstalled;   // non si capisce se c'è: come se non ci fosse
            }
            storeStates[store.Info.Id] = status;
            SourceResult? result = null;
            if (status == StoreStatus.Active)
            {
                // Con "solo installati" i giochi da installare restano fuori (e i loro segnaposto si tolgono).
                result = Read(store.Info.Name, SourceKind.Store, store.Info.Id,
                              () => store.Scan().Where(g => g.IsInstalled || settings.IncludeNotInstalled).ToList());
                sources.Add(result);
            }
            reports.Add(new StoreReport(store.Info, status, result));
        }

        // 5. Il confronto, e le tue spunte.
        ImportPlan plan = ImportPlan.Create(library, sources, storeStates, settings.IncludeNotInstalled);
        plan.ApplySelection(settings);
        plan.Stores.AddRange(reports);

        plan.Log.AddRange(log);
        foreach (SourceResult s in plan.Sources)
        {
            plan.Log.Add(s.Error is not null
                ? $"{s.Name}: can't be read ({s.Error})"
                : s.IsStore ? $"{s.Name}: {s.Games!.Count} games, {s.Games.Count(g => g.IsInstalled)} installed"
                            : $"{s.Name}: {s.Games!.Count} games");
        }
        foreach (StoreReport r in reports.Where(r => r.Status != StoreStatus.Active))
            plan.Log.Add($"{r.Info.Name}: {(r.Status == StoreStatus.Disabled ? "turned off" : "not installed")}");
        return plan;
    }

    // Legge una fonte senza far fermare tutto se qualcosa va storto: un errore diventa una fonte
    // "fallita" (le sue voci in Steam restano come sono).
    private static SourceResult Read(string name, SourceKind kind, string id, Func<List<Game>> scan)
    {
        try
        {
            return new SourceResult(name, kind, id, scan());
        }
        catch (Exception ex)
        {
            return new SourceResult(name, kind, id, Games: null, Error: ex.Message);
        }
    }
}
