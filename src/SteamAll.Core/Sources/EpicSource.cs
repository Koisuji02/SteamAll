// =====================================================================================
//  EpicSource.cs
//  La fonte "Epic Games": i giochi che possiedi su Epic, installati e non.
//
//  - La libreria: Data\Catalog\catcache.bin, la cache del launcher di Epic. È un JSON scritto in
//    base64 (un modo di scrivere byte qualsiasi con sole lettere e cifre). Si aggiorna quando il
//    launcher è aperto.
//  - Gli installati: Data\Manifests\*.item, un file JSON per ogni gioco installato.
//  Teniamo solo i giochi per Windows: niente DLC, add-on, editor o beta.
// =====================================================================================

using System.Runtime.Versioning;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SteamAll.Core.Games;
using SteamAll.Core.Platform;

namespace SteamAll.Core.Sources;

/// <summary>Un gioco Epic installato: la sua cartella e il suo exe ("" se il manifest non lo dice).</summary>
public sealed record EpicInstall(string Folder, string Exe);

// Solo Scan e FindDataFolder usano il registro di Windows: sono loro ad avere [SupportedOSPlatform].
// La lettura del catalogo e dei manifest funziona ovunque (e le prove la usano anche altrove).
public static class EpicSource
{
    public const string SourceName = "Epic Games";
    public const string Protocol = "com.epicgames.launcher";

    /// <summary>I giochi di Epic; null se Epic non è installato (così la fonte non compare).</summary>
    [SupportedOSPlatform("windows")]
    public static List<Game>? Scan(IPlatform platform)
    {
        string? data = FindDataFolder();
        if (data is null || !platform.IsProtocolRegistered(Protocol)) return null;
        string cache = Path.Combine(data, "Catalog", "catcache.bin");
        if (!File.Exists(cache)) return null;

        // byte -> testo base64 -> byte veri -> testo JSON -> albero JSON
        string base64 = Encoding.ASCII.GetString(Files.ReadShared(cache)).Trim();
        string json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        JsonNode catalog = JsonHelpers.TryParse(json)
            ?? throw new InvalidDataException("the Epic library cache can't be read");

        return ParseCatalog(catalog, ReadManifests(Path.Combine(data, "Manifests")));
    }

    /// <summary>
    /// Dalla cache del catalogo ai giochi. È separato da Scan (e "internal") così le prove possono
    /// dargli un catalogo finto, senza Epic installato.
    /// </summary>
    /// <param name="installed">CatalogItemId dei giochi installati -> cartella ed exe.</param>
    internal static List<Game> ParseCatalog(JsonNode catalog, IReadOnlyDictionary<string, EpicInstall> installed)
    {
        var games = new List<Game>();
        var namespaceOf = new Dictionary<string, string>();   // il "namespace" Epic di ogni gioco, serve sotto

        foreach (JsonNode item in catalog.Items())
        {
            // Solo la categoria "games", non gli "addons", e niente DLC (un DLC ha un "gioco principale").
            List<string> categories = item.Items("categories").Select(c => c.Text("path")).ToList();
            if (!categories.Contains("games") || categories.Contains("addons")) continue;
            if (item.Field("mainGameItem").Text("id").Length > 0) continue;

            // La versione per Windows: da lì prendiamo l'AppName, il nome interno del gioco.
            // Any (LINQ) = "almeno un elemento rispetta la condizione".
            JsonNode? windows = item.Items("releaseInfo").FirstOrDefault(r =>
                r.Text("appId").Length > 0 &&
                r.Items("platform").Any(p => p.AsText() == "Windows"));
            string appName = windows.Text("appId");

            string title = item.Text("title");
            string ns = item.Text("namespace");
            string catalogId = item.Text("id");
            if (appName.Length == 0 || title.Length == 0 || ns.Length == 0 || catalogId.Length == 0) continue;

            string key = "epic:" + catalogId;
            // TryAdd aggiunge solo se la chiave non c'è già: la cache a volte ripete lo stesso gioco.
            if (!namespaceOf.TryAdd(key, ns)) continue;

            // Immagini dello store, ripiego per quando SteamGridDB non ha niente (Fase 2).
            var images = new Dictionary<string, string>();
            foreach (JsonNode image in item.Items("keyImages"))
            {
                string url = image.Text("url");
                switch (image.Text("type"))   // si può fare switch anche su un testo
                {
                    case "DieselGameBoxTall": images["p"] = url; break;
                    case "DieselGameBox": images["wide"] = url; images["hero"] = url; break;
                    case "DieselGameBoxLogo": images["logo"] = url; break;
                }
            }

            // TryGetValue: se il gioco è tra gli installati, mette la cartella in "folder".
            bool isInstalled = installed.TryGetValue(catalogId, out EpicInstall? install);
            games.Add(new Game
            {
                Key = key,
                // A volte Epic scrive "?" al posto di ® e ™: "Ghostrunner?" -> "Ghostrunner".
                // (?<=...) e (?=...) guardano cosa c'è prima e dopo il "?" senza prenderlo.
                Name = Regex.Replace(title, @"(?<=[A-Za-z0-9])\?(?=$|[ :])", "").Trim(),
                Source = SourceName,
                Origin = GameOrigin.Store,
                StoreId = "epic",
                ProviderId = "epic",
                Kind = "Epic",
                IsInstalled = isInstalled,
                InstallFolder = install?.Folder ?? "",
                ExePath = install?.Exe ?? "",   // l'exe vero del gioco: serve per l'icona e per riconoscere la voce
                EpicAppName = appName,
                // Il link che apre Epic sul gioco. %3A è il carattere ":" scritto "da link".
                StoreUri = $"com.epicgames.launcher://apps/{ns}%3A{catalogId}%3A{appName}?action=launch",
                StoreImages = images,
            });
        }

        // Editor, strumenti e beta di un gioco: stesso namespace e titolo che inizia con quello
        // del gioco seguito da uno spazio ("Ghostrunner" e "Ghostrunner Demo"): restano fuori.
        var extras = games
            .Where(b => games.Any(a => a != b && namespaceOf[a.Key] == namespaceOf[b.Key]
                                        && b.Name.StartsWith(a.Name + " ", StringComparison.OrdinalIgnoreCase)))
            .Select(b => b.Key)
            .ToHashSet();   // HashSet = un insieme senza doppioni, in cui cercare è velocissimo
        return games.Where(g => !extras.Contains(g.Key)).ToList();
    }

    /// <summary>I giochi installati: CatalogItemId -> cartella ed exe (solo installazioni complete e cartelle che esistono).</summary>
    internal static Dictionary<string, EpicInstall> ReadManifests(string folder)
    {
        var result = new Dictionary<string, EpicInstall>();
        if (!Directory.Exists(folder)) return result;
        foreach (string file in Directory.GetFiles(folder, "*.item"))
        {
            JsonNode? manifest = JsonHelpers.TryParse(File.ReadAllText(file, Encoding.UTF8));
            if (manifest is null || manifest.Flag("bIsIncompleteInstall")) continue;   // installazione a metà
            string location = manifest.Text("InstallLocation");
            if (location.Length == 0 || !Directory.Exists(location)) continue;
            // LaunchExecutable è relativo alla cartella del gioco (es. "Binaries\Win64\Game.exe").
            string exe = manifest.Text("LaunchExecutable");
            result[manifest.Text("CatalogItemId")] = new EpicInstall(location, exe.Length > 0 ? Path.Combine(location, exe) : "");
        }
        return result;
    }

    // Dove Epic scrive nel registro la sua cartella dati (prima la versione a 32 bit, la più comune).
    private static readonly string[] RegistryKeys =
        [@"SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher", @"SOFTWARE\Epic Games\EpicGamesLauncher"];

    // Epic scrive la sua cartella dati nel registro; se manca, quella predefinita.
    [SupportedOSPlatform("windows")]
    private static string? FindDataFolder()
    {
        foreach (string keyPath in RegistryKeys)
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key?.GetValue("AppDataPath") is string path && Directory.Exists(path)) return path;
        }
        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                                       "Epic", "EpicGamesLauncher", "Data");
        return Directory.Exists(fallback) ? fallback : null;
    }
}
