// =====================================================================================
//  SteamApplier.cs
//  "Apply to Steam": porta il piano dentro Steam. In ordine:
//    1. i launcher dei giochi emulati (e i dati del loro controller);
//    2. le immagini che mancano (SteamGridDB, poi lo store), mentre Steam è ancora aperto;
//    3. se la libreria cambia: chiude Steam SUBITO, rilegge shortcuts.vdf (Steam lo riscrive
//       quando si chiude), applica aggiunte, modifiche e rimozioni, salva con la copia di sicurezza
//       e riapre Steam;
//    4. la pulizia: le immagini rimaste di giochi tolti vanno nel Cestino.
//  Le modifiche si preparano come "operazioni" legate all'app id, così valgono anche sul file riletto.
// =====================================================================================

using SteamAll.Core.Artwork;
using SteamAll.Core.Games;
using SteamAll.Core.Launchers;
using SteamAll.Core.Plan;
using SteamAll.Core.Platform;
using SteamAll.Core.Settings;
using SteamAll.Core.Steam;
using SteamAll.Launcher;

namespace SteamAll.Core.Apply;

/// <summary>Cosa farà Apply (per la finestra di conferma).</summary>
public sealed record ApplySummary(int Add, int Update, int Remove, int Launchers, bool SteamWillRestart);

/// <summary>Com'è andata.</summary>
public sealed record ApplyReport(int Added, int Updated, int Removed, int LaunchersWritten, int ImagesWritten,
                                 int ImagesMissing, int CleanedFiles, bool SteamRestarted, List<string> Log, List<string> Warnings);

public sealed class SteamApplier(IPlatform platform, HttpClient http)
{
    /// <summary>Quanto aspettare che Steam si chiuda.</summary>
    public TimeSpan SteamShutdownTimeout { get; init; } = TimeSpan.FromSeconds(60);

    // Le operazioni sulla libreria, legate all'app id della voce.
    private abstract record Operation(uint AppId);
    private sealed record AddOp(uint AppId, ShortcutSpec Spec) : Operation(AppId);
    private sealed record UpdateOp(uint AppId, ShortcutSpec Spec, bool OnlyMarker) : Operation(AppId);
    private sealed record RemoveOp(uint AppId) : Operation(AppId);
    // "record X(...) : Operation(AppId)" = un record che eredita da un altro e gli passa AppId.

    // Una riga del piano con quello che bisogna fare.
    private sealed record Work(PlanEntry Entry, ShortcutSpec? Spec, List<Operation> Operations);

    /// <summary>Il riepilogo per la conferma: nessuna modifica, solo conti.</summary>
    public ApplySummary Summarize(ImportPlan plan, AppSettings settings)
    {
        List<Work> work = Prepare(plan, out _);
        List<Operation> ops = work.SelectMany(w => w.Operations).ToList();
        int launchers = plan.Entries.Count(e => IsIncludedFolderEmulated(e) && LauncherInstaller.Check(e.Game, settings.ManageControllers, platform.Os) != LauncherState.UpToDate)
                      + UsedSetups(plan, settings).Count(s => LauncherInstaller.CheckSetup(s, settings.ManageControllers, platform.Os) != LauncherState.UpToDate);
        int changes = ops.OfType<AddOp>().Count() + ops.OfType<RemoveOp>().Count() + ops.OfType<UpdateOp>().Count(o => NeedsUpdate(plan.Library, o));
        return new ApplySummary(ops.OfType<AddOp>().Count(), ops.OfType<UpdateOp>().Count(o => NeedsUpdate(plan.Library, o)),
                                ops.OfType<RemoveOp>().Count(), launchers, changes > 0 && platform.IsSteamRunning());
    }

    public async Task<ApplyReport> ApplyAsync(ImportPlan plan, AppSettings settings, string artworkStateFile, IProgress<string>? progress = null)
    {
        var log = new List<string>();
        var warnings = new List<string>();
        void Say(string line) { log.Add(line); progress?.Report(line); }
        void Warn(string line) { warnings.Add(line); progress?.Report("⚠ " + line); }

        ShortcutLibrary library = plan.Library ?? throw new InvalidOperationException("Steam's library was not found: nothing can be applied");

        // --- 1. Launcher dei giochi emulati -----------------------------------------------
        int launchers = 0;
        var skipped = new HashSet<PlanEntry>();
        foreach (PlanEntry e in plan.Entries.Where(IsIncludedFolderEmulated))
        {
            LauncherResult r = LauncherInstaller.Install(e.Game, settings.ManageControllers, platform.Os);
            if (r.State == LauncherState.Failed)
            {
                Warn($"{e.Game.Name}: {r.Message}");
                if (e.Shortcut is null) skipped.Add(e);   // senza launcher una voce nuova non partirebbe: la saltiamo
            }
            else if (r.State != LauncherState.UpToDate)
            {
                launchers++;
                Say($"{e.Game.Name}: {r.Message}");
            }
        }
        // Gli emulatori installati: un launcher per emulatore, per tutti i suoi giochi.
        foreach (EmulatorSetup setup in UsedSetups(plan, settings))
        {
            string name = setup.Emulator?.Name ?? setup.EmulatorId;
            LauncherResult r = LauncherInstaller.InstallSetup(setup, settings.ManageControllers, platform.Os);
            if (r.State == LauncherState.Failed)
            {
                Warn($"{name}: {r.Message}");
                skipped.UnionWith(plan.Entries.Where(e => e.Game.EmulatorSetupId == setup.Id && e.Shortcut is null));
            }
            else if (r.State != LauncherState.UpToDate)
            {
                launchers++;
                Say($"{name}: {r.Message}");
            }
        }

        // --- 2. Le operazioni sulla libreria ---------------------------------------------
        List<Work> work = Prepare(plan, out List<string> notes).Where(w => !skipped.Contains(w.Entry)).ToList();
        foreach (string n in notes) Warn(n);

        // --- 3. Le immagini (con Steam ancora aperto: scaricare richiede tempo) ------------
        int imagesWritten = 0, imagesMissing = 0;
        var client = new SteamGridDbClient(http, settings.GetSteamGridDbKey(platform));
        if (!client.HasKey) Warn("No SteamGridDB key (Settings): only the store images are used");
        var artwork = new ArtworkService(client, platform, library.GridFolder, artworkStateFile, settings.HeroHd);
        foreach (Work w in work.Where(w => w.Spec is not null && w.Entry.Status is SteamStatus.New or SteamStatus.InSteam))
        {
            uint appId = w.Entry.Shortcut?.AppId ?? w.Spec!.AppId;
            ArtworkResult result = await artwork.EnsureAsync(new ArtworkJob(appId, w.Entry.Game.Name, w.Entry.Game.StoreImages,
                                                                           LegacyHeroHash(w.Entry, library.User.Id), Choices(settings, w.Entry)));
            imagesWritten += result.Written;
            imagesMissing += result.Missing;
            if (result.Written > 0) Say($"{w.Entry.Game.Name}: {result.Written} image(s)" + (result.Message.Length > 0 ? $" ({result.Message})" : ""));
            // I segnaposto usano come icona quella scaricata (prima avevano quella dello store).
            if (result.IconFile is not null && w.Entry.Game.IsInstalled == false)
            {
                for (int i = 0; i < w.Operations.Count; i++)
                {
                    w.Operations[i] = w.Operations[i] switch
                    {
                        AddOp a => a with { Spec = a.Spec with { Icon = result.IconFile } },
                        UpdateOp u => u with { Spec = u.Spec with { Icon = result.IconFile } },
                        Operation other => other,
                    };
                }
            }
        }

        // --- 4. La libreria di Steam -------------------------------------------------------
        List<Operation> operations = work.SelectMany(w => w.Operations).ToList();
        bool anyChange = operations.Any(o => o is not UpdateOp u || NeedsUpdate(library, u));
        int added = 0, updated = 0, removed = 0;
        bool restarted = false;
        if (anyChange)
        {
            bool wasRunning = platform.IsSteamRunning();
            if (wasRunning)
            {
                Say("Closing Steam...");
                if (!await platform.ShutdownSteamAsync(SteamShutdownTimeout))
                    throw new InvalidOperationException("Steam didn't close: close it yourself and press Apply again");
            }
            try
            {
                // Rileggiamo il file: chiudendosi Steam può averlo riscritto (tempo di gioco, ultime partite...).
                ShortcutLibrary fresh = ShortcutLibrary.Load(library.User);
                (added, updated, removed) = ApplyOperations(fresh, operations);
                fresh.Save();
                Say($"Steam library saved: {added} added, {updated} updated, {removed} removed (backup: shortcuts.vdf.steamall-backup)");
            }
            finally
            {
                // Anche se il salvataggio fallisce, Steam va riaperto ("finally" viene eseguito sempre).
                if (wasRunning)
                {
                    platform.StartSteam();
                    restarted = true;
                    Say("Steam started again");
                }
            }
        }
        else
        {
            Say("Steam library already up to date");
        }

        // --- 5. Pulizia -------------------------------------------------------------------
        int cleaned = 0;
        if (settings.Cleanup)
        {
            ShortcutLibrary final = ShortcutLibrary.Load(library.User);
            cleaned = GridCleanup.Clean(final, platform, Warn);
            if (cleaned > 0) Say($"Cleanup: {cleaned} leftover image(s) moved to the Recycle Bin");
        }

        return new ApplyReport(added, updated, removed, launchers, imagesWritten, imagesMissing, cleaned, restarted, log, warnings);
    }

    // Setup_BoilR ricordava l'hero che aveva scritto in <Gioco>\Setup_BoilR\stato.ini ("Hero.<utente>=hash"):
    // se c'è, un hero diverso da quello l'hai scelto tu, e resta.
    private static string? LegacyHeroHash(PlanEntry e, string userId)
    {
        if (e.Game.Origin != GameOrigin.Folder || e.Game.Source == ImportPlan.LeftoversSource || e.Game.InstallFolder.Length == 0) return null;
        Dictionary<string, string> state = ControllerSwitch.ReadState(Path.Combine(e.Game.InstallFolder, "Setup_BoilR"));
        return state.TryGetValue("Hero." + userId, out string? hash) ? hash : null;
    }

    // Le immagini scelte per questo gioco nella sua pagina di dettaglio (tipo -> indirizzo).
    // Enum.TryParse: dal testo salvato ("Hero") al valore ArtworkKind.Hero; i testi sconosciuti si ignorano.
    private static Dictionary<ArtworkKind, string>? Choices(AppSettings settings, PlanEntry entry) =>
        settings.ArtworkChoices.TryGetValue(entry.Game.Key, out Dictionary<string, string>? chosen)
            ? chosen.Where(c => Enum.TryParse(c.Key, out ArtworkKind _)).ToDictionary(c => Enum.Parse<ArtworkKind>(c.Key), c => c.Value)
            : null;

    // Un gioco che andrà (o resta) in Steam e che gestiamo noi.
    private static bool IsIncluded(PlanEntry e) =>
        e.Problem is null && e.Status is SteamStatus.New or SteamStatus.InSteam && (e.Shortcut is null || e.Shortcut.IsManaged);

    // Gioco emulato di una cartella: ha il suo launcher nella cartella del gioco.
    private static bool IsIncludedFolderEmulated(PlanEntry e) =>
        e.Game.Origin == GameOrigin.Folder && e.Game.Emulator is not null && IsIncluded(e);

    // Gli emulatori installati che hanno almeno un gioco in Steam: a loro serve il launcher.
    private static List<EmulatorSetup> UsedSetups(ImportPlan plan, AppSettings settings)
    {
        var ids = plan.Entries.Where(e => e.Game.Origin == GameOrigin.Emulator && IsIncluded(e)).Select(e => e.Game.EmulatorSetupId).ToHashSet();
        return settings.Emulators.Where(s => ids.Contains(s.Id)).ToList();
    }

    // Per ogni riga del piano: la voce che deve avere e le operazioni da fare.
    private List<Work> Prepare(ImportPlan plan, out List<string> notes)
    {
        notes = [];
        var work = new List<Work>();
        foreach (PlanEntry e in plan.Entries)
        {
            var ops = new List<Operation>();
            ShortcutSpec? spec = null;
            switch (e.Status)
            {
                case SteamStatus.New:
                    spec = LaunchCommands.For(e.Game, platform, out string reason);
                    if (spec is null) { notes.Add($"{e.Game.Name}: not added ({reason})"); break; }
                    ops.Add(new AddOp(spec.AppId, spec));
                    if (e.ReplacedPlaceholder is not null) ops.Add(new RemoveOp(e.ReplacedPlaceholder.AppId));
                    break;

                case SteamStatus.InSteam when e.Shortcut is { IsManaged: true } s:
                    // Una voce già nostra (o di BoilR, o di Setup_BoilR): la teniamo con il suo app id
                    // e la "prendiamo in carico". Giochi delle cartelle e segnaposto si allineano anche
                    // nel comando; per i giochi installati degli store cambiamo solo il marchio (il loro
                    // comando funziona già, meglio non toccarlo).
                    if (e.Game.Source == ImportPlan.LeftoversSource) break;   // voce rimasta ancora valida: com'è
                    spec = LaunchCommands.For(e.Game, platform, out _);
                    if (spec is null) break;
                    bool onlyMarker = e.Game.Origin == GameOrigin.Store && e.Game.IsInstalled;
                    ops.Add(new UpdateOp(s.AppId, spec, onlyMarker));
                    break;

                case SteamStatus.WillBeRemoved:
                    if (e.Shortcut is { IsManaged: true }) ops.Add(new RemoveOp(e.Shortcut.AppId));
                    if (e.ReplacedPlaceholder is not null) ops.Add(new RemoveOp(e.ReplacedPlaceholder.AppId));
                    break;
            }
            work.Add(new Work(e, spec, ops));
        }
        return work;
    }

    // Una modifica cambia davvero qualcosa? (Altrimenti non serve chiudere Steam.)
    private static bool NeedsUpdate(ShortcutLibrary? library, UpdateOp op)
    {
        VdfNode? node = library?.FindEntry(op.AppId)?.Child;
        if (node is null) return false;
        var copy = new VdfNode();
        copy.Entries.AddRange(node.Entries.Select(e => new VdfEntry { Type = e.Type, KeyBytes = e.KeyBytes, Data = e.Data, Child = e.Child }));
        return Update(copy, op);
    }

    private static bool Update(VdfNode node, UpdateOp op)
    {
        if (!op.OnlyMarker) return op.Spec.UpdateNode(node);
        if (node.GetText("DevkitGameID") == op.Spec.Marker) return false;
        node.SetText("DevkitGameID", op.Spec.Marker);
        return true;
    }

    // Applica le operazioni alla libreria riletta. Restituisce tre numeri insieme (una tupla).
    private static (int Added, int Updated, int Removed) ApplyOperations(ShortcutLibrary library, List<Operation> operations)
    {
        int added = 0, updated = 0, removed = 0;
        foreach (RemoveOp op in operations.OfType<RemoveOp>())
        {
            VdfEntry? entry = library.FindEntry(op.AppId);
            if (entry is null) continue;
            library.List.Entries.Remove(entry);
            removed++;
        }
        foreach (UpdateOp op in operations.OfType<UpdateOp>())
        {
            VdfNode? node = library.FindEntry(op.AppId)?.Child;
            if (node is not null && Update(node, op)) updated++;
        }
        foreach (AddOp op in operations.OfType<AddOp>())
        {
            if (library.FindEntry(op.AppId) is not null) continue;   // c'è già (per esempio un doppio Apply)
            library.List.Entries.Add(VdfEntry.FromMap("0", op.Spec.ToNode()));   // il nome giusto lo dà Save
            added++;
        }
        return (added, updated, removed);
    }
}
