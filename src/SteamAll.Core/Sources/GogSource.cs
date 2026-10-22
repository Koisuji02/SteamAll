// =====================================================================================
//  GogSource.cs
//  La fonte "GOG GALAXY": i giochi della tua libreria GOG, installati e non, letti dal database
//  di GALAXY (C:\ProgramData\GOG.com\Galaxy\storage\galaxy-2.0.db).
//
//  GALAXY raccoglie anche i giochi delle piattaforme che gli hai collegato (Steam, Ubisoft, EA,
//  Epic...): quelli NON li prendiamo. Ogni store legge la sua libreria e apre il suo programma
//  (UbisoftStore, EaStore...): così "Installa" in Steam apre subito lo store giusto, senza passare
//  da GALAXY. Restano fuori anche i DLC, i doppioni che GOG stesso non mostra, i giochi che hai
//  nascosto in GALAXY e i contenuti extra (colonne sonore, artbook: niente sviluppatori né generi).
// =====================================================================================

using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SteamAll.Core.Games;
using SteamAll.Core.Platform;

namespace SteamAll.Core.Sources;

// Solo Scan usa SQLite di Windows: è lui ad avere [SupportedOSPlatform]. FromRows funziona ovunque.
public static class GogSource
{
    public const string SourceName = "GOG GALAXY";
    public const string Protocol = "goggalaxy";

    /// <summary>I giochi di GALAXY; null se GALAXY non è installato.</summary>
    [SupportedOSPlatform("windows")]
    public static List<Game>? Scan(IPlatform platform)
    {
        string db = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                                 "GOG.com", "Galaxy", "storage", "galaxy-2.0.db");
        if (!File.Exists(db) || !platform.IsProtocolRegistered(Protocol)) return null;

        // Lavoriamo su una copia, in una cartella temporanea tutta nostra: GALAXY può tenere il
        // database aperto, e nel suo non vogliamo mai scrivere. Guid.NewGuid() = un nome unico.
        string temp = Path.Combine(Path.GetTempPath(), "steamall-galaxy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string copy = Path.Combine(temp, "galaxy.db");
            File.WriteAllBytes(copy, Files.ReadShared(db));
            if (File.Exists(db + "-wal")) File.WriteAllBytes(copy + "-wal", Files.ReadShared(db + "-wal"));

            List<string?[]> rows = Sqlite.Query(copy, LibraryQuery);

            // I giochi GOG installati: releaseKey -> cartella.
            var installed = new Dictionary<string, string>();
            foreach (string?[] r in Sqlite.Query(copy, "SELECT productId, installationPath FROM InstalledBaseProducts"))
                installed["gog_" + r[0]] = r[1] ?? "";

            return FromRows(rows, installed);
        }
        finally
        {
            // La copia temporanea è nostra: si cancella sempre (non serve il Cestino).
            // Se non ci si riesce, resta nella cartella Temp di Windows: non è grave.
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Dalle righe della query ai giochi. "internal" per le prove, che gli danno righe finte.
    /// Colonne: 0 releaseKey, 1 title, 2 originalTitle, 3 originalMeta, 4 originalImages,
    /// 5 isDlc, 6 isVisibleInLibrary, 7 isHidden.
    /// </summary>
    internal static List<Game> FromRows(IEnumerable<string?[]> rows, IReadOnlyDictionary<string, string> installed)
    {
        var games = new List<Game>();
        foreach (string?[] r in rows)
        {
            string releaseKey = r[0] ?? "";
            if (!releaseKey.StartsWith("gog_", StringComparison.Ordinal)) continue;   // steam_, uplay_, epic_...: non di GOG
            if (r[5] == "1" || r[6] == "0" || r[7] == "1") continue;                   // DLC, invisibile, nascosto

            // Il titolo è dentro un piccolo JSON: {"title":"GreedFall"}. Prima "title", poi "originalTitle".
            string title = new[] { r[1], r[2] }
                .Select(t => JsonHelpers.TryParse(t).Text("title").Trim())
                .FirstOrDefault(t => t.Length > 0) ?? "";
            if (title.Length == 0) continue;

            // Contenuti extra: hanno i metadati, ma senza sviluppatori e senza generi.
            JsonNode? meta = JsonHelpers.TryParse(r[3]);
            if (meta is not null && !meta.Items("developers").Any() && !meta.Items("genres").Any()) continue;

            // Le immagini di GOG sono in formato WebP, che Steam non legge: cambiando ".webp" in
            // ".png" nell'indirizzo, il server di GOG manda la stessa immagine in PNG.
            JsonNode? pictures = JsonHelpers.TryParse(r[4]);
            var images = new Dictionary<string, string>();
            AddImage(images, "p", pictures.Text("verticalCover"));
            AddImage(images, "hero", pictures.Text("background"));
            AddImage(images, "icon", pictures.Text("squareIcon"));

            bool isInstalled = installed.TryGetValue(releaseKey, out string? folder);
            games.Add(new Game
            {
                Key = releaseKey,
                Name = title,
                Source = SourceName,
                Origin = GameOrigin.Store,
                StoreId = "gog",
                ProviderId = "gog",
                Kind = "GOG",
                IsInstalled = isInstalled,
                InstallFolder = folder ?? "",
                StoreUri = "goggalaxy://openGameView/" + releaseKey,
                StoreImages = images,
            });
        }
        return games;
    }

    private static void AddImage(Dictionary<string, string> images, string kind, string url)
    {
        // "$1" nel testo di sostituzione = quello che ha catturato il primo gruppo tra parentesi.
        if (url.Length > 0) images[kind] = Regex.Replace(url, @"\.webp(\?|$)", ".png$1");
    }

    // La query della libreria: un releaseKey per riga, con titolo, metadati e immagini (che il
    // database tiene in una tabella a parte, GamePieces) e le proprietà della release.
    // """ ... """ è una "stringa grezza" su più righe: si scrive così com'è, senza caratteri speciali.
    private const string LibraryQuery = """
        SELECT r.releaseKey,
          (SELECT p.value FROM GamePieces p JOIN GamePieceTypes t ON t.id = p.gamePieceTypeId
            WHERE p.releaseKey = r.releaseKey AND t.type = 'title' LIMIT 1),
          (SELECT p.value FROM GamePieces p JOIN GamePieceTypes t ON t.id = p.gamePieceTypeId
            WHERE p.releaseKey = r.releaseKey AND t.type = 'originalTitle' LIMIT 1),
          (SELECT p.value FROM GamePieces p JOIN GamePieceTypes t ON t.id = p.gamePieceTypeId
            WHERE p.releaseKey = r.releaseKey AND t.type = 'originalMeta' LIMIT 1),
          (SELECT p.value FROM GamePieces p JOIN GamePieceTypes t ON t.id = p.gamePieceTypeId
            WHERE p.releaseKey = r.releaseKey AND t.type = 'originalImages' LIMIT 1),
          (SELECT isDlc FROM ReleaseProperties WHERE releaseKey = r.releaseKey),
          (SELECT isVisibleInLibrary FROM ReleaseProperties WHERE releaseKey = r.releaseKey),
          (SELECT MAX(isHidden) FROM UserReleaseProperties WHERE releaseKey = r.releaseKey)
        FROM (SELECT DISTINCT releaseKey FROM LibraryReleases) r
        """;
}
