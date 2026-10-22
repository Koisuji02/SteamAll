// =====================================================================================
//  GogGalaxyStore.cs
//  GOG GALAXY (Windows): la libreria GOG, installata e non, dal database di GALAXY (vedi
//  GogSource). Solo i giochi GOG: quelli delle piattaforme collegate a GALAXY (Ubisoft, EA...)
//  li legge e li apre il loro store, senza passare da GALAXY.
//  Un gioco installato parte con il suo exe, quello che GOG indica nel file goggame-<id>.info.
// =====================================================================================

using System.Runtime.Versioning;
using System.Text;
using System.Text.Json.Nodes;
using SteamAll.Core.Apply;
using SteamAll.Core.Games;
using SteamAll.Core.Platform;
using SteamAll.Core.Sources;
using SteamAll.Core.Steam;

namespace SteamAll.Core.Stores;

[SupportedOSPlatform("windows")]
public sealed class GogGalaxyStore(IPlatform platform) : IStoreProvider
{
    public StoreInfo Info { get; } = new("gog", "GOG GALAXY", "https://www.gog.com/galaxy", "https://www.gog.com/apple-touch-icon.png", GogSource.Protocol);

    public bool IsInstalled() => platform.IsProtocolRegistered(GogSource.Protocol);

    public List<Game> Scan() =>
        GogSource.Scan(platform) ?? throw new InvalidDataException("GOG GALAXY's database was not found: open GOG GALAXY once");

    public ShortcutSpec? LaunchCommand(Game game, out string reason)
    {
        reason = "";
        GogPlayTask? task = ReadPlayTask(game.InstallFolder, game.Key["gog_".Length..]);
        if (task is null)
        {
            reason = "GOG didn't say which exe starts the game (no goggame-*.info with a primary play task)";
            return null;
        }
        return new ShortcutSpec(game.Name, task.Exe, task.WorkingDir, task.Arguments, task.Icon, Markers.For("gog"), true);
    }

    /// <summary>Cosa avvia GOG per un gioco installato: exe, cartella di lavoro, parametri, icona.</summary>
    public sealed record GogPlayTask(string Exe, string WorkingDir, string Arguments, string Icon);

    /// <summary>
    /// Legge goggame-&lt;productId&gt;.info nella cartella del gioco: un JSON con "playTasks", dove
    /// quello "isPrimary" è il gioco vero (gli altri sono manuali, configuratori...).
    /// "internal static": non usa la piattaforma, e le prove lo chiamano con file finti.
    /// </summary>
    internal static GogPlayTask? ReadPlayTask(string folder, string productId)
    {
        string info = Path.Combine(folder, $"goggame-{productId}.info");
        if (!File.Exists(info))
            info = Directory.Exists(folder) ? Directory.GetFiles(folder, "goggame-*.info").FirstOrDefault() ?? "" : "";
        if (info.Length == 0) return null;

        JsonNode? json = JsonHelpers.TryParse(File.ReadAllText(info, Encoding.UTF8));
        JsonNode? task = json.Items("playTasks").FirstOrDefault(t => t.Flag("isPrimary") && t.Text("path").Length > 0)
                      ?? json.Items("playTasks").FirstOrDefault(t => t.Text("type") == "FileTask" && t.Text("path").Length > 0);
        if (task is null) return null;

        string exe = Path.GetFullPath(Path.Combine(folder, task.Text("path")));
        string workingDir = task.Text("workingDir").Length > 0 ? Path.GetFullPath(Path.Combine(folder, task.Text("workingDir"))) : folder;
        string ico = Path.ChangeExtension(info, ".ico");
        return new GogPlayTask(exe, workingDir, task.Text("arguments"), File.Exists(ico) ? ico : exe);
    }
}
