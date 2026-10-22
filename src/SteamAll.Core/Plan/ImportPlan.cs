// =====================================================================================
//  ImportPlan.cs
//  Il "piano": confronta i giochi di tutte le fonti con la libreria di Steam e decide, per
//  ognuno, cosa succede (New, In Steam, Will be removed, Skipped). Qui NON si legge e NON si
//  scrive niente su disco: riceve i dati già letti (lo fa PlanBuilder) e li confronta. Così le
//  prove possono dargli dati finti e controllare ogni caso.
//
//  Come BoilR, le voci "nostre" (di SteamAll, di BoilR, di Setup_BoilR) che non servono più si
//  tolgono: giochi disinstallati, giochi che lo store non elenca più, e tutte le voci degli store
//  spenti o non installati (anche i segnaposto dei giochi da installare).
// =====================================================================================

using System.Text.RegularExpressions;
using SteamAll.Core.Games;
using SteamAll.Core.Settings;
using SteamAll.Core.Steam;
using SteamAll.Core.Stores;

namespace SteamAll.Core.Plan;

/// <summary>Che tipo di fonte è.</summary>
public enum SourceKind { Folder, Emulator, Store }

/// <summary>Lo stato di uno store su questo computer.</summary>
public enum StoreStatus { Active, Disabled, NotInstalled }

/// <summary>
/// Il risultato della lettura di una fonte.
/// Error non null: la lettura è fallita, e le sue voci già in Steam restano come sono.
/// </summary>
/// <param name="Id">Lo store ("epic"...), l'emulatore installato (EmulatorSetup.Id) o "folder".</param>
public sealed record SourceResult(string Name, SourceKind Kind, string Id, List<Game>? Games, string? Error = null)
{
    public bool IsStore => Kind == SourceKind.Store;
}

/// <summary>Uno store per la pagina Stores: com'è messo e cosa si è letto.</summary>
public sealed record StoreReport(StoreInfo Info, StoreStatus Status, SourceResult? Result);

public sealed class ImportPlan
{
    /// <summary>La "fonte" delle voci già in Steam che nessuna fonte elenca più.</summary>
    public const string LeftoversSource = "Earlier imports";

    /// <summary>La libreria di Steam confrontata (null se Steam non c'è o non si legge).</summary>
    public ShortcutLibrary? Library { get; }

    /// <summary>Le fonti lette, nell'ordine (comprese quelle fallite).</summary>
    public IReadOnlyList<SourceResult> Sources { get; }

    /// <summary>Tutte le righe del piano, nell'ordine delle fonti; per ultime le voci rimaste.</summary>
    public IReadOnlyList<PlanEntry> Entries { get; }

    /// <summary>Tutti gli store di questo sistema, anche spenti e non installati (li riempie PlanBuilder).</summary>
    public List<StoreReport> Stores { get; } = [];

    /// <summary>Messaggi per il registro dell'interfaccia.</summary>
    public List<string> Log { get; } = [];

    private ImportPlan(ShortcutLibrary? library, IReadOnlyList<SourceResult> sources, IReadOnlyList<PlanEntry> entries)
    {
        Library = library;
        Sources = sources;
        Entries = entries;
    }

    /// <summary>
    /// Costruisce il piano dai dati già letti. "stores" = lo stato di ogni store (id -> stato):
    /// le voci degli store spenti o non installati vengono tolte.
    /// </summary>
    /// <param name="includeNotInstalled">false = "solo giochi installati": i segnaposto già in Steam si tolgono.</param>
    public static ImportPlan Create(ShortcutLibrary? library, IEnumerable<SourceResult> sources,
                                    IReadOnlyDictionary<string, StoreStatus>? stores = null, bool includeNotInstalled = true)
    {
        List<SourceResult> present = sources.Where(s => s.Games is not null || s.Error is not null).ToList();

        // Una riga del piano per ogni gioco, nell'ordine delle fonti.
        // SelectMany "appiattisce": da un elenco di elenchi a un elenco solo.
        List<PlanEntry> entries = present
            .SelectMany(s => s.Games ?? [])
            .Select(g => new PlanEntry { Game = g })
            .ToList();

        MarkStoreDuplicates(entries.Where(e => e.Game.Origin == GameOrigin.Store).ToList());
        MarkEmulatorDuplicates(entries);

        var matcher = new Matcher(library, present, stores ?? new Dictionary<string, StoreStatus>(), includeNotInstalled);
        foreach (PlanEntry e in entries) matcher.Match(e, entries);
        entries.AddRange(matcher.Leftovers(entries));

        return new ImportPlan(library, present, entries);
    }

    /// <summary>
    /// Lo stesso gioco in due store conta una volta sola: prima quello installato, poi il primo
    /// nell'ordine degli store (GOG prima di Epic). Gli altri diventano "Skipped".
    /// </summary>
    private static void MarkStoreDuplicates(List<PlanEntry> storeEntries)
    {
        var seen = new Dictionary<string, PlanEntry>();
        // OrderByDescending(installato) mette prima gli installati; a parità resta l'ordine di prima.
        foreach (PlanEntry e in storeEntries.OrderByDescending(e => e.Game.IsInstalled))
        {
            string key = Names.Normalize(e.Game.Name);
            if (key.Length == 0) continue;
            if (seen.TryGetValue(key, out PlanEntry? first))
                e.PlanProblem = $"Same game as in {first.Game.Source}" + (first.Game.IsInstalled ? " (installed)" : "");
            else
                seen[key] = e;
        }
    }

    /// <summary>
    /// Un gioco degli emulatori installati che c'è già in una cartella dei giochi, o in un altro
    /// emulatore con lo stesso nome, si salta: in Steam ci va una volta sola.
    /// </summary>
    private static void MarkEmulatorDuplicates(List<PlanEntry> entries)
    {
        var folderNames = entries.Where(e => e.Game.Origin == GameOrigin.Folder && e.Game.Problem is null)
                                 .Select(e => Names.Normalize(e.Game.Name)).ToHashSet();
        var seen = new Dictionary<string, PlanEntry>();
        foreach (PlanEntry e in entries.Where(e => e.Game.Origin == GameOrigin.Emulator))
        {
            string key = Names.Normalize(e.Game.Name);
            if (folderNames.Contains(key)) e.PlanProblem = "The same game is in one of your game folders";
            else if (seen.TryGetValue(key, out PlanEntry? first)) e.PlanProblem = $"Same game as in {first.Game.Source}";
            else seen[key] = e;
        }
    }

    /// <summary>
    /// Applica le tue scelte salvate: spunte dei giochi e delle cartelle. I giochi nuovi partono
    /// con la spunta (tutta la libreria è selezionata finché non togli qualcosa).
    /// </summary>
    public void ApplySelection(AppSettings settings)
    {
        foreach (PlanEntry e in Entries)
        {
            e.IsChecked = !settings.ExcludedGames.Contains(e.Game.Key);
            e.IsSourceEnabled = e.Game.Origin != GameOrigin.Folder || !settings.ExcludedSources.Contains(e.Game.Source);
        }
    }

    // =================================================================================
    //  Matcher: abbina ogni gioco alla sua voce di Steam. È una classe "annidata" (dentro
    //  ImportPlan) perché serve solo qui. Ricorda le voci già abbinate (used), così una voce non
    //  finisce a due giochi; alla fine quelle "nostre" rimaste senza gioco sono le Leftovers.
    // =================================================================================
    private sealed class Matcher
    {
        private readonly ShortcutLibrary? library;
        private readonly List<Shortcut> shortcuts;
        private readonly HashSet<Shortcut> used = [];
        private readonly IReadOnlyDictionary<string, StoreStatus> stores;
        private readonly HashSet<string> failedStores;          // store che non si sono letti
        private readonly List<string> failedFolders;             // cartelle dei giochi che non si sono lette
        private readonly bool emulatorFailed;                    // un emulatore installato non si è letto
        private readonly HashSet<string>? installedEpicApps;     // null se Epic non è stato letto
        private readonly bool includeNotInstalled;               // false = "solo giochi installati"

        public Matcher(ShortcutLibrary? library, List<SourceResult> sources, IReadOnlyDictionary<string, StoreStatus> stores, bool includeNotInstalled)
        {
            this.library = library;   // "this.library" = il campo; "library" da solo = il parametro
            this.stores = stores;
            this.includeNotInstalled = includeNotInstalled;
            shortcuts = library?.Shortcuts ?? [];
            failedStores = sources.Where(s => s.IsStore && s.Error is not null).Select(s => s.Id).ToHashSet();
            failedFolders = sources.Where(s => s.Kind == SourceKind.Folder && s.Error is not null).Select(s => WithSlash(s.Name)).ToList();
            emulatorFailed = sources.Any(s => s.Kind == SourceKind.Emulator && s.Error is not null);

            SourceResult? epic = sources.FirstOrDefault(s => s.IsStore && s.Id == "epic" && s.Games is not null);
            if (epic is not null)
                installedEpicApps = epic.Games!.Where(g => g.IsInstalled).Select(g => g.EpicAppName).ToHashSet();
        }

        public void Match(PlanEntry entry, List<PlanEntry> all)
        {
            Game game = entry.Game;
            if (game.Origin != GameOrigin.Store) MatchLocalGame(entry);
            else if (game.IsInstalled) MatchInstalledStoreGame(entry);
            else MatchNotInstalledStoreGame(entry, all);
        }

        // Gioco di una cartella o di un emulatore installato: la voce con lo stesso app id (exe +
        // nome), altrimenti con lo stesso nome.
        private void MatchLocalGame(PlanEntry entry)
        {
            Game game = entry.Game;
            uint appId = SteamAppId.ForShortcut(game.ExePath, game.Name);
            entry.AppId = appId;
            // Le lambda "s => ..." qui sono la condizione che FindUnused deve controllare.
            Shortcut? s = FindUnused(s => s.AppId == appId)
                       ?? FindUnused(s => !s.IsPlaceholder && string.Equals(s.Name, game.Name, StringComparison.OrdinalIgnoreCase));
            Attach(entry, s);
            if (s is null) entry.CoverFile = library?.FindImage(appId, "p");   // immagini di un import precedente
        }

        // Gioco di uno store, installato: la voce che lo avvia (di BoilR o nostra), oppure una con lo
        // stesso nome (magari l'hai aggiunto tu a mano). Se aveva un segnaposto, quello va tolto.
        private void MatchInstalledStoreGame(PlanEntry entry)
        {
            Game game = entry.Game;
            Shortcut? s = FindUnused(s => !s.IsPlaceholder && s.IsManaged && !IsDead(s) && LaunchesGame(s, game))
                       ?? FindUnused(s => !s.IsPlaceholder && Names.Normalize(s.Name) == Names.Normalize(game.Name));
            Attach(entry, s);

            Shortcut? placeholder = FindUnused(s => s.IsPlaceholder && IsPlaceholderOf(s, game));
            if (placeholder is null) return;
            used.Add(placeholder);
            entry.ReplacedPlaceholder = placeholder;
            entry.CoverFile ??= library?.FindImage(placeholder.AppId, "p");   // "??=" = solo se ancora null
            if (s is null) entry.PlanNote = "Installed: replaces its Install placeholder";
        }

        // Gioco di uno store, NON installato: diventa (o resta) un segnaposto con il tasto Installa,
        // a meno che lo stesso gioco ci sia già (in Steam, o in una cartella dei giochi).
        private void MatchNotInstalledStoreGame(PlanEntry entry, List<PlanEntry> all)
        {
            Game game = entry.Game;
            if (entry.Problem is not null) return;   // doppione di un altro store
            string name = Names.Normalize(game.Name);

            if (all.Any(e => e.Game.Origin != GameOrigin.Store && e.Game.Problem is null && Names.Normalize(e.Game.Name) == name))
            {
                entry.PlanProblem = "The same game is in one of your game folders";
                return;
            }

            // Il suo segnaposto (anche uno vecchio, per esempio di Setup_BoilR tramite GOG GALAXY).
            Shortcut? placeholder = FindUnused(s => s.IsPlaceholder && IsPlaceholderOf(s, game));
            if (placeholder is not null)
            {
                Attach(entry, placeholder);
                return;
            }

            // Una voce "nostra" dello stesso store e con lo stesso nome, che avviava il gioco quando era
            // installato: lo hai disinstallato. Diventa il segnaposto (stesso app id: immagini e raccolte restano).
            Shortcut? stale = FindUnused(s => !s.IsPlaceholder && s.IsManaged && StoreCatalog.Identify(s) == game.StoreId && Names.Normalize(s.Name) == name);
            if (stale is not null)
            {
                Attach(entry, stale);
                entry.PlanNote = "No longer installed: becomes an Install placeholder";
                return;
            }

            if (shortcuts.Any(s => !s.IsPlaceholder && !IsDead(s) && Names.Normalize(s.Name) == name))
                entry.PlanProblem = "A game with the same name is already in Steam";
        }

        /// <summary>Le voci "nostre" (di BoilR, di Setup_BoilR o di SteamAll) rimaste senza un gioco.</summary>
        public List<PlanEntry> Leftovers(List<PlanEntry> all)
        {
            // Le righe degli store per link: servono a spiegare perché un segnaposto va tolto.
            // ToLookup è come un dizionario, ma accetta più righe con lo stesso link.
            ILookup<string, PlanEntry> byUri = all.Where(e => e.Game.StoreUri.Length > 0).ToLookup(e => e.Game.StoreUri);
            var result = new List<PlanEntry>();
            foreach (Shortcut s in shortcuts.Where(s => s.IsManaged && !used.Contains(s)))
            {
                string? store = StoreCatalog.Identify(s);
                var entry = new PlanEntry
                {
                    Game = new Game
                    {
                        Key = $"steam:{s.AppId}",
                        Name = s.Name,
                        Source = LeftoversSource,
                        Origin = store is null or Markers.FolderStore ? GameOrigin.Folder
                               : store == Markers.EmulatorStore ? GameOrigin.Emulator : GameOrigin.Store,
                        StoreId = store ?? "",
                        Kind = s.IsPlaceholder ? "Install placeholder" : s.IsBoilR ? "Added by BoilR" : "Added by SteamAll",
                        IsInstalled = !s.IsPlaceholder,
                        StoreUri = s.StoreUri,
                    },
                };
                Attach(entry, s);
                (entry.MustRemove, entry.PlanNote, entry.PlanProblem) = Decide(s, store, byUri);   // tre valori insieme ("tupla")
                result.Add(entry);
            }
            return result;
        }

        // Cosa fare di una voce rimasta: (da togliere?, nota, problema).
        private (bool Remove, string? Note, string? Problem) Decide(Shortcut s, string? store, ILookup<string, PlanEntry> byUri)
        {
            StoreStatus? status = store is not null && stores.TryGetValue(store, out StoreStatus st) ? st : null;
            if (status == StoreStatus.NotInstalled) return (true, "Its store is not installed on this computer", null);
            if (status == StoreStatus.Disabled) return (true, "Its store is turned off in SteamAll", null);
            if (s.IsPlaceholder && !includeNotInstalled) return (true, "SteamAll is set to installed games only", null);
            if (store is not null && failedStores.Contains(store)) return (false, null, "Its store can't be read now: left as is");
            if (s.IsPlaceholder) return (true, PlaceholderReason(s, byUri), null);
            // Una cartella dei giochi che non si legge (un disco esterno staccato?): i suoi giochi restano.
            if (failedFolders.Any(f => WithSlash(s.ExePath).StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                return (false, null, "Its game folder can't be read now: left as is");
            if (IsDead(s)) return (true, "The game is no longer installed", null);
            if (store == Markers.EmulatorStore)
                return emulatorFailed ? (false, null, "Its emulator can't be read now: left as is")
                                      : (true, "Its emulator is off, or the game is no longer in its folders", null);
            if (status == StoreStatus.Active && s.IsSteamAll) return (true, "No longer installed or no longer in the store library", null);
            return (false, "Still installed, but no source lists it", null);
        }

        // --- piccoli aiuti --------------------------------------------------------------

        // Predicate<Shortcut> = un metodo che riceve una voce e risponde true o false.
        // Prima le voci che gestiamo noi, poi le altre: a parità, meglio riprendere le nostre.
        private Shortcut? FindUnused(Predicate<Shortcut> condition) =>
            shortcuts.Where(s => !used.Contains(s) && condition(s))
                     .OrderByDescending(s => s.IsManaged)
                     .FirstOrDefault();

        private void Attach(PlanEntry entry, Shortcut? s)
        {
            if (s is null) return;
            used.Add(s);
            entry.Shortcut = s;
            entry.AppId = s.AppId;
            entry.CoverFile = library?.FindImage(s.AppId, "p");
            if (!s.IsManaged) entry.PlanNote = "Added to Steam by hand: left as is";
        }

        // Il segnaposto di questo gioco: lo stesso link, oppure stesso store e stesso nome (i segnaposto
        // di una volta aprivano GOG GALAXY anche per i giochi Ubisoft, EA...).
        private static bool IsPlaceholderOf(Shortcut s, Game game) =>
            s.StoreUri == game.StoreUri
            || (StoreCatalog.Identify(s) == game.StoreId && Names.Normalize(s.Name) == Names.Normalize(game.Name));

        // La voce avvia questo gioco installato? L'exe o la cartella di avvio sono dentro la cartella
        // del gioco, oppure le opzioni contengono il suo link (o, per Epic, il suo AppName).
        private static bool LaunchesGame(Shortcut s, Game game)
        {
            if (game.InstallFolder.Length > 0)
            {
                string folder = WithSlash(game.InstallFolder);
                if (WithSlash(s.ExePath).StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return true;
                if (WithSlash(s.StartDir.Trim().Trim('"')).StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return true;
            }
            if (game.StoreUri.Length > 0 && s.LaunchOptions.Contains(game.StoreUri, StringComparison.OrdinalIgnoreCase)) return true;
            // Regex.Escape mette "\" davanti ai caratteri speciali del nome, così vale alla lettera.
            return game.EpicAppName.Length > 0 &&
                   Regex.IsMatch(s.LaunchOptions, "%3A" + Regex.Escape(game.EpicAppName) + "(\\?|\"|$)");
        }

        private static string WithSlash(string path) => path.Replace('/', '\\').TrimEnd('\\') + "\\";

        // Una voce di un gioco che non c'è più: l'exe è sparito, oppure è un link di Epic a un gioco
        // non installato (questo lo sappiamo solo se Epic è stato letto).
        private bool IsDead(Shortcut s)
        {
            if (s.IsPlaceholder) return false;
            string exe = s.ExePath;
            if (exe.Length > 0 && !exe.Contains("://") && Path.IsPathRooted(exe) && !File.Exists(exe)) return true;
            if (installedEpicApps is null) return false;
            Match m = Regex.Match(s.LaunchOptions, @"com\.epicgames\.launcher://apps/[^?\s""]*%3A([^?\s""%]+)\?");
            return m.Success && !installedEpicApps.Contains(m.Groups[1].Value);
        }

        // Perché un segnaposto non serve più: il gioco non è più nello store, oppure il piano lo salta
        // (doppione, già in Steam con un altro nome...).
        private static string PlaceholderReason(Shortcut s, ILookup<string, PlanEntry> byUri) =>
            byUri[s.StoreUri].FirstOrDefault() is { } e   // "is { } e" = "non è null, e chiamalo e"
                ? e.Problem ?? "Not needed any more"
                : "No longer in the store library";
    }
}
