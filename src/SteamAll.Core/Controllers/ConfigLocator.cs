// =====================================================================================
//  ConfigLocator.cs
//  Dove ogni emulatore tiene la configurazione del controller (la cartella che ControllerSwitch
//  cambia). Dipende da com'è installato:
//
//    copia "portable" (Windows, cartelle dei giochi, o un emulatore installato in modo portable):
//      Ryujinx  <emulatore>\portable                       (Config.json)
//      Cemu     <emulatore>\portable\controllerProfiles
//      Dolphin  <emulatore>\User\Config                    (se accanto c'è portable.txt)
//      RPCS3    <emulatore>\config\input_configs           (su Windows RPCS3 è sempre così)
//    installato nel sistema:
//      Ryujinx  Windows %APPDATA%\Ryujinx           Linux ~/.config/Ryujinx        macOS ~/Library/Application Support/Ryujinx
//      Cemu     Windows %APPDATA%\Cemu\controllerProfiles   Linux ~/.config/Cemu/controllerProfiles
//               macOS ~/Library/Application Support/Cemu/controllerProfiles
//      Dolphin  Windows %APPDATA%\Dolphin Emulator\Config (o Documenti\Dolphin Emulator\Config)
//               Linux ~/.config/dolphin-emu (Flatpak: ~/.var/app/org.DolphinEmu.dolphin-emu/config/dolphin-emu)
//               macOS ~/Library/Application Support/Dolphin/Config
//      RPCS3    Linux ~/.config/rpcs3/input_configs  macOS ~/Library/Application Support/rpcs3/input_configs
//
//  Restituisce null se la cartella non c'è ancora (l'emulatore non è mai stato aperto) o se il
//  controller di quell'emulatore non si gestisce: in "reason" il perché, per la scheda Controllers.
// =====================================================================================

using SteamAll.Core.Platform;

namespace SteamAll.Core.Controllers;

public static class ConfigLocator
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string MacSupport => Path.Combine(Home, "Library", "Application Support");

    /// <summary>La cartella della configurazione per una copia portable nella cartella di un gioco.</summary>
    public static string? ForPortable(string gameDir, string kind, out string reason)
    {
        reason = "";
        switch (kind)
        {
            case "Ryujinx":
                if (Directory.Exists(Path.Combine(gameDir, "portable"))) return Path.Combine(gameDir, "portable");
                reason = "Ryujinx is not portable (no portable folder): it uses the shared setup in AppData";
                return null;
            case "Cemu":
                if (Directory.Exists(Path.Combine(gameDir, "portable"))) return Path.Combine(gameDir, "portable", "controllerProfiles");
                reason = "Cemu is not portable (no portable folder): it uses the shared setup";
                return null;
            case "Dolphin":
                if (File.Exists(Path.Combine(gameDir, "portable.txt"))) return Path.Combine(gameDir, "User", "Config");
                reason = "Dolphin is not portable (no portable.txt): it uses the shared setup";
                return null;
            case "RPCS3":
                return Path.Combine(gameDir, "config", "input_configs");
            default:
                reason = "the emulator maps controllers by itself";
                return null;
        }
    }

    /// <summary>La cartella della configurazione di un emulatore installato nel sistema.</summary>
    public static string? ForInstalled(string emulatorExe, string kind, OsKind os, out string reason)
    {
        reason = "";
        if (kind.Length == 0)
        {
            reason = "the emulator maps controllers by itself";
            return null;
        }
        // Prima la copia portable accanto al programma: se c'è, vince (come fa l'emulatore stesso).
        string exeDir = Path.GetDirectoryName(emulatorExe) ?? "";
        if (exeDir.Length > 0 && (os == OsKind.Windows || kind != "RPCS3"))
        {
            string? portable = ForPortable(exeDir, kind, out _);
            if (portable is not null && (kind != "RPCS3" || Directory.Exists(Path.Combine(exeDir, "config")))) return portable;
        }

        string[] candidates = (kind, os) switch
        {
            ("Ryujinx", OsKind.Windows) => [Path.Combine(AppData, "Ryujinx")],
            ("Ryujinx", OsKind.MacOS) => [Path.Combine(MacSupport, "Ryujinx")],
            ("Ryujinx", _) => [Path.Combine(Home, ".config", "Ryujinx"), Path.Combine(Home, ".var", "app", "org.ryujinx.Ryujinx", "config", "Ryujinx")],
            ("Cemu", OsKind.Windows) => [Path.Combine(AppData, "Cemu")],
            ("Cemu", OsKind.MacOS) => [Path.Combine(MacSupport, "Cemu")],
            ("Cemu", _) => [Path.Combine(Home, ".config", "Cemu"), Path.Combine(Home, ".var", "app", "info.cemu.Cemu", "config", "Cemu")],
            ("Dolphin", OsKind.Windows) => [Path.Combine(AppData, "Dolphin Emulator", "Config"),
                                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Dolphin Emulator", "Config")],
            ("Dolphin", OsKind.MacOS) => [Path.Combine(MacSupport, "Dolphin", "Config")],
            ("Dolphin", _) => [Path.Combine(Home, ".config", "dolphin-emu"), Path.Combine(Home, ".var", "app", "org.DolphinEmu.dolphin-emu", "config", "dolphin-emu")],
            ("RPCS3", OsKind.Windows) => [Path.Combine(exeDir, "config")],
            ("RPCS3", OsKind.MacOS) => [Path.Combine(MacSupport, "rpcs3")],
            ("RPCS3", _) => [Path.Combine(Home, ".config", "rpcs3"), Path.Combine(Home, ".var", "app", "net.rpcs3.RPCS3", "config", "rpcs3")],
            _ => [],
        };
        string? found = candidates.FirstOrDefault(Directory.Exists);
        if (found is null)
        {
            reason = candidates.Length == 0 ? "controllers are not managed for this emulator" : "its settings folder was not found: start the emulator once";
            return null;
        }
        return kind switch
        {
            "Cemu" => Path.Combine(found, "controllerProfiles"),
            "RPCS3" => Path.Combine(found, "input_configs"),
            _ => found,
        };
    }
}
