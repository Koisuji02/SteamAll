// =====================================================================================
//  UbisoftStore.cs
//  Ubisoft Connect (Windows), letto come fanno Playnite e BoilR, senza login:
//
//  - La libreria (anche i giochi NON installati): la cache delle configurazioni dei giochi che
//    Ubisoft Connect scarica per il tuo account,
//        %LOCALAPPDATA%\Ubisoft Game Launcher\cache\configuration\configurations
//    (nelle versioni vecchie nella cartella di Ubisoft Connect). È un file protobuf: un elenco di
//    giochi, ognuno con uplay_id, install_id e la sua configurazione in YAML (nome, immagini, exe).
//    Teniamo solo i giochi veri: con un exe da avviare (start_game), non contenuti aggiuntivi
//    (is_ulc, addons) e non giochi di altre piattaforme (third_party_platform, es. Steam).
//  - Gli installati: il registro, HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\<id>\InstallDir.
//
//  Si avviano con Ubisoft Connect (upc.exe) e il link uplay://launch/<id>/0; un gioco non
//  installato apre Ubisoft Connect sulla sua installazione con uplay://install/<id>.
// =====================================================================================

using System.Runtime.Versioning;
using Microsoft.Win32;
using SteamAll.Core.Apply;
using SteamAll.Core.Games;
using SteamAll.Core.Platform;
using SteamAll.Core.Sources;
using SteamAll.Core.Steam;

namespace SteamAll.Core.Stores;

[SupportedOSPlatform("windows")]
public sealed class UbisoftStore(IPlatform platform) : IStoreProvider
{
    private const string LauncherKey = @"SOFTWARE\WOW6432Node\Ubisoft\Launcher";
    private const string UninstallKey = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string AssetsUrl = "https://ubistatic3-a.akamaihd.net/orbit/uplay_launcher_3_0/assets/";

    public StoreInfo Info { get; } = new("ubisoft", "Ubisoft Connect", "https://www.ubisoft.com/ubisoft-connect", "https://www.ubisoft.com/favicon.ico", "uplay");

    // La cartella di Ubisoft Connect: dal registro, altrimenti quella predefinita.
    private static string ClientFolder
    {
        get
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(LauncherKey);
            if (key?.GetValue("InstallDir") is string dir && Directory.Exists(dir)) return dir;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ubisoft", "Ubisoft Game Launcher");
        }
    }

    private string? ClientExe =>
        platform.ProtocolExe("uplay") ?? (File.Exists(Path.Combine(ClientFolder, "upc.exe")) ? Path.Combine(ClientFolder, "upc.exe") : null);

    public bool IsInstalled() => platform.IsProtocolRegistered("uplay") || File.Exists(Path.Combine(ClientFolder, "upc.exe"));

    public List<Game> Scan()
    {
        Dictionary<string, string> installs = ReadInstalls();

        string? cache = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ubisoft Game Launcher", "cache", "configuration", "configurations"),
            Path.Combine(ClientFolder, "cache", "configuration", "configurations"),
        }.FirstOrDefault(File.Exists);
        List<UbisoftProduct> products = cache is null ? [] : ParseCache(Files.ReadShared(cache));

        var games = new List<Game>();
        var seen = new HashSet<string>();
        foreach (UbisoftProduct p in products)
        {
            // Installato? Il registro usa l'id del gioco (di solito uplay_id e install_id coincidono).
            string? installId = new[] { p.UplayId, p.InstallId }.FirstOrDefault(installs.ContainsKey);
            if (!seen.Add(p.UplayId)) continue;
            if (installId is not null) seen.Add(installId);
            games.Add(NewGame(p.UplayId, p.Name, installId, installId is null ? "" : installs[installId], p.Images));
        }
        // Installati che la cache non conosce: il nome dalla voce di disinstallazione di Windows.
        foreach ((string id, string folder) in installs)
        {
            if (seen.Contains(id)) continue;
            using RegistryKey? uninstall = Registry.LocalMachine.OpenSubKey($@"{UninstallKey}\Uplay Install {id}");
            string name = uninstall?.GetValue("DisplayName") as string ?? Path.GetFileName(folder);
            games.Add(NewGame(id, name, id, folder, new Dictionary<string, string>()));
        }
        return games;
    }

    private Game NewGame(string id, string name, string? installId, string folder, IReadOnlyDictionary<string, string> images) => new()
    {
        Key = "ubisoft:" + id,
        Name = name,
        Source = Info.Name,
        Origin = GameOrigin.Store,
        StoreId = "ubisoft",
        ProviderId = "ubisoft",
        Kind = "Ubisoft",
        IsInstalled = installId is not null,
        InstallFolder = folder,
        // Installato: si avvia; non installato: Ubisoft Connect apre la sua installazione.
        StoreUri = installId is not null ? $"uplay://launch/{installId}/0" : $"uplay://install/{id}",
        StoreImages = images,
    };

    // I giochi installati: id -> cartella (solo cartelle che esistono).
    private static Dictionary<string, string> ReadInstalls()
    {
        var result = new Dictionary<string, string>();
        using RegistryKey? installs = Registry.LocalMachine.OpenSubKey(LauncherKey + @"\Installs");
        if (installs is null) return result;
        foreach (string id in installs.GetSubKeyNames())
        {
            using RegistryKey? key = installs.OpenSubKey(id);
            string folder = (key?.GetValue("InstallDir") as string ?? "").Replace('/', '\\').TrimEnd('\\');
            if (folder.Length > 0 && Directory.Exists(folder)) result[id] = folder;
        }
        return result;
    }

    /// <summary>Un gioco della cache: id, nome già tradotto e immagini.</summary>
    internal sealed record UbisoftProduct(string UplayId, string InstallId, string Name, IReadOnlyDictionary<string, string> Images);

    /// <summary>
    /// Legge la cache "configurations". Il messaggio principale ha un elenco di giochi (campo 1);
    /// ogni gioco ha uplay_id (1), install_id (2) e la configurazione YAML (3).
    /// "internal static" per le prove, che gli danno una cache finta.
    /// </summary>
    internal static List<UbisoftProduct> ParseCache(byte[] data)
    {
        var products = new List<UbisoftProduct>();
        var addons = new HashSet<string>();
        var candidates = new List<(string UplayId, string InstallId, Dictionary<string, List<string>> Yaml)>();
        foreach (Protobuf.Field entry in Protobuf.Read(data).Where(f => f.Number == 1 && f.WireType == 2))
        {
            List<Protobuf.Field> fields = Protobuf.Read(entry.Bytes);
            string uplayId = fields.FirstOrDefault(f => f.Number == 1 && f.WireType == 0).Number64.ToString();
            string installId = fields.FirstOrDefault(f => f.Number == 2 && f.WireType == 0).Number64.ToString();
            string yaml = fields.FirstOrDefault(f => f.Number == 3 && f.WireType == 2).Text;
            if (uplayId == "0" || yaml.Length == 0) continue;
            Dictionary<string, List<string>> values = MiniYaml.Flatten(yaml);
            // I contenuti aggiuntivi di un gioco sono elencati in root.addons: quegli id non sono giochi.
            if (values.TryGetValue("root.addons[].id", out List<string>? ids)) addons.UnionWith(ids);
            candidates.Add((uplayId, installId, values));
        }

        foreach ((string uplayId, string installId, Dictionary<string, List<string>> y) in candidates)
        {
            if (addons.Contains(uplayId)) continue;
            if (!y.Has("root.start_game")) continue;                     // niente da avviare: non è un gioco
            if (y.Has("root.third_party_platform")) continue;            // un gioco di Steam o di un'altra piattaforma
            if (y.First("root.is_ulc") is "yes" or "true") continue;     // contenuto aggiuntivo

            string name = Localized(y, y.First("root.name")).Trim();
            if (name.Length == 0) continue;
            var images = new Dictionary<string, string>();
            void Image(string kind, string field)
            {
                string file = Localized(y, y.First("root." + field));
                if (file.Length > 0) images[kind] = file.Contains("://") ? file : AssetsUrl + file;
            }
            Image("p", "thumb_image");
            Image("hero", "background_image");
            Image("logo", "logo_image");
            products.Add(new UbisoftProduct(uplayId, installId, CleanName(name), images));
        }
        return products;
    }

    // I testi possono essere "chiavi" di traduzione (es. "l1") con il valore in localizations.default.
    private static string Localized(Dictionary<string, List<string>> y, string value) =>
        value.Length > 0 && y.First("localizations.default." + value) is { Length: > 0 } text ? text : value;

    private static string CleanName(string name) => name.Replace("™", "").Replace("®", "").Replace("©", "").Trim();

    public ShortcutSpec? LaunchCommand(Game game, out string reason)
    {
        reason = "";
        string? client = ClientExe;
        if (client is null)
        {
            reason = "Ubisoft Connect not found";
            return null;
        }
        // Come BoilR: Ubisoft Connect con il link del gioco (non il "programma per aprire i link",
        // così il plugin del tasto Installa non lo scambia per un gioco da installare).
        return new ShortcutSpec(game.Name, client, Path.GetDirectoryName(client)!, game.StoreUri, client, Markers.For("ubisoft"), true);
    }
}
