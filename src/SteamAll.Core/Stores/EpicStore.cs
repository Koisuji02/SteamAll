// =====================================================================================
//  EpicStore.cs
//  Epic Games (Windows): la libreria dalla cache del launcher, gli installati dai manifest (vedi
//  EpicSource). Un gioco installato si avvia con il launcher di Epic e il link del gioco, così
//  Epic controlla licenza, aggiornamenti e salvataggi nel cloud come quando lo avvii da Epic.
// =====================================================================================

using System.Runtime.Versioning;
using SteamAll.Core.Apply;
using SteamAll.Core.Games;
using SteamAll.Core.Platform;
using SteamAll.Core.Sources;
using SteamAll.Core.Steam;

namespace SteamAll.Core.Stores;

[SupportedOSPlatform("windows")]
public sealed class EpicStore(IPlatform platform) : IStoreProvider
{
    public StoreInfo Info { get; } = new("epic", "Epic Games", "https://store.epicgames.com/download",
        "https://static-assets-prod.epicgames.com/epic-store/static/favicon.ico", EpicSource.Protocol);

    public bool IsInstalled() => platform.IsProtocolRegistered(EpicSource.Protocol);

    public List<Game> Scan() =>
        EpicSource.Scan(platform) ?? throw new InvalidDataException("Epic's library cache was not found: open the Epic Games Launcher once");

    public ShortcutSpec? LaunchCommand(Game game, out string reason)
    {
        reason = "";
        string? launcher = platform.ProtocolExe(EpicSource.Protocol);
        if (launcher is null)
        {
            reason = "Epic Games Launcher not found";
            return null;
        }
        // "silent=true": Epic avvia il gioco senza mostrare la sua finestra.
        string icon = File.Exists(game.ExePath) ? game.ExePath : launcher;
        return new ShortcutSpec(game.Name, launcher, Path.GetDirectoryName(launcher)!, game.StoreUri + "&silent=true", icon, Markers.For("epic"), true);
    }
}
