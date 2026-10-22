// =====================================================================================
//  LaunchCommands.cs
//  Come Steam deve avviare ogni gioco (come fa BoilR, più i giochi non installati):
//    - gioco di una cartella: il suo exe (per i giochi emulati il launcher; su Linux e macOS
//      SteamAll stesso con "--launch");
//    - gioco di un emulatore installato: il launcher dell'emulatore con il file del gioco;
//    - gioco installato di uno store: lo decide il suo store (IStoreProvider.LaunchCommand);
//    - gioco NON installato: un "segnaposto" che apre il suo store sulla pagina del gioco (con il
//      "programma per aprire i link" del sistema: così il plugin del tasto Installa lo riconosce).
// =====================================================================================

using SteamAll.Core.Games;
using SteamAll.Core.Launchers;
using SteamAll.Core.Platform;
using SteamAll.Core.Steam;
using SteamAll.Core.Stores;

namespace SteamAll.Core.Apply;

public static class LaunchCommands
{
    /// <summary>
    /// La voce di Steam per questo gioco, o null se non si sa come avviarlo (in "reason" il perché).
    /// "out string reason" = il metodo restituisce anche un secondo valore, in quella variabile.
    /// </summary>
    public static ShortcutSpec? For(Game game, IPlatform platform, out string reason)
    {
        reason = "";
        switch (game.Origin)
        {
            case GameOrigin.Folder:
                return FolderGame(game, platform);

            case GameOrigin.Emulator:
                // Windows: il launcher dell'emulatore con il gioco; Linux e macOS: SteamAll --launch.
                string options = platform.Os == OsKind.Windows
                    ? Quote(game.RomPath)
                    : $"--launch {Quote(LauncherInstaller.SetupConfigFile(game.EmulatorSetupId))} {Quote(game.RomPath)}";
                return new ShortcutSpec(game.Name, game.ExePath, Path.GetDirectoryName(game.ExePath) ?? "", options,
                                        game.Icon, Markers.Emulator, true);

            default:
                if (!game.IsInstalled) return Placeholder(game, platform);
                IStoreProvider? store = StoreCatalog.Find(platform, game.ProviderId);
                if (store is null)
                {
                    reason = $"the store \"{game.ProviderId}\" is not available on this computer";
                    return null;
                }
                return store.LaunchCommand(game, out reason);
        }
    }

    private static ShortcutSpec FolderGame(Game game, IPlatform platform)
    {
        // Un gioco emulato su Linux e macOS: lo avvia SteamAll ("--launch"), con il launcher.ini del gioco.
        if (game.Emulator is not null && platform.Os != OsKind.Windows)
            return new ShortcutSpec(game.Name, game.ExePath, game.InstallFolder, $"--launch {Quote(LauncherInstaller.FolderConfigFile(game))}",
                                    game.EmulatorExe, Markers.Folder, true);
        return new ShortcutSpec(game.Name, game.ExePath, game.StartDir, "", game.ExePath, Markers.Folder, true);
    }

    /// <summary>Il segnaposto di un gioco non installato: apre lo store sulla sua pagina.</summary>
    public static ShortcutSpec Placeholder(Game game, IPlatform platform)
    {
        (string opener, string prefix) = platform.LinkOpener;   // "deconstruction": la tupla in due variabili
        string protocol = game.StoreUri.Split("://")[0];
        // L'icona di riserva è quella dello store; se SteamGridDB ne ha una, l'Applier la cambia.
        return new ShortcutSpec(game.Name, opener, Path.GetDirectoryName(opener)!, prefix + game.StoreUri,
                                platform.ProtocolExe(protocol) ?? "", Markers.ForPlaceholder(game.StoreId), false);
    }

    private static string Quote(string path) => $"\"{path}\"";
}
