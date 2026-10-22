// =====================================================================================
//  GameLauncher.cs
//  Il launcher dei giochi emulati su Linux e macOS: lì Steam avvia SteamAll stesso,
//      SteamAll --launch "<cartella>/SteamAll/launcher.ini" ["<file del gioco>"] [--settings]
//  e SteamAll, senza aprire la finestra, fa quello che su Windows fa il piccolo launcher
//  (SteamAll.Launcher): legge launcher.ini, mette la configurazione del controller giusta
//  (da Steam o fuori da Steam) e avvia l'emulatore, aspettando che finisca così Steam vede il
//  gioco in esecuzione. "--settings" apre l'emulatore senza il gioco.
// =====================================================================================

using System.Diagnostics;
using System.Text;
using SteamAll.Launcher;

namespace SteamAll.Core.Launchers;

public static class GameLauncher
{
    /// <summary>Esegue "--launch". Restituisce il codice di uscita dell'emulatore (1 se non parte).</summary>
    public static int Run(IReadOnlyList<string> args)
    {
        // args[0] = "--launch", args[1] = launcher.ini, poi eventualmente il gioco e "--settings".
        if (args.Count < 2) return Fail("usage: SteamAll --launch <launcher.ini> [game file] [--settings]");
        string ini = Path.GetFullPath(args[1]);
        string baseDir = Path.GetDirectoryName(Path.GetDirectoryName(ini)!)!;   // la cartella sopra "SteamAll"
        Dictionary<string, string> config = LauncherConfig.Read(ini);
        string exe = LauncherConfig.Resolve(baseDir, LauncherConfig.Value(config, "Emulator"));
        if (exe.Length == 0) return Fail($"{ini} has no settings: open SteamAll and press Apply to Steam");

        bool settingsOnly = args.Skip(2).Any(a => a == "--settings");
        string rom = LauncherConfig.Resolve(baseDir, LauncherConfig.Value(config, "Game"));
        if (rom.Length == 0) rom = args.Skip(2).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) is { } r ? Path.GetFullPath(r) : "";
        if (rom.Length == 0) settingsOnly = true;
        if (!File.Exists(exe)) return Fail("emulator not found: " + exe);
        if (!settingsOnly && !File.Exists(rom)) return Fail("game file not found: " + rom);

        string mode = ControllerSwitch.DetectMode(Environment.GetEnvironmentVariable, out string details);
        string dataDir = LauncherConfig.Resolve(baseDir, LauncherConfig.Value(config, "DataDir"));
        if (dataDir.Length == 0) dataDir = Path.Combine(baseDir, ControllerSwitch.DataFolder);
        string controller = LauncherConfig.Value(config, "Controller"), outcome = "";
        if (controller.Length > 0)
        {
            try { outcome = ControllerSwitch.Prepare(LauncherConfig.Resolve(baseDir, LauncherConfig.Value(config, "ConfigDir")), dataDir, controller, mode); }
            catch (Exception ex) { outcome = $"not prepared ({ex.Message})"; }
        }

        string arguments = settingsOnly ? "" : LauncherConfig.CommandLine(LauncherConfig.Arguments(config), rom);
        WriteLastStart(dataDir, mode, details, outcome, settingsOnly, $"\"{exe}\" {arguments}");
        try
        {
            // UseShellExecute = false: avvia il programma direttamente, con questi parametri.
            using Process process = Process.Start(new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
            }) ?? throw new InvalidOperationException("the emulator didn't start");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Fail($"can't start the emulator: {ex.Message}");
        }
    }

    // Senza finestra, l'errore va sullo "standard error" (lo si vede avviando da un terminale).
    private static int Fail(string message)
    {
        Console.Error.WriteLine("SteamAll: " + message);
        return 1;
    }

    private static void WriteLastStart(string dataDir, string mode, string details, string outcome, bool settingsOnly, string command)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append($"SteamAll - last start of this game: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            sb.Append("Started: ").Append(mode == ControllerSwitch.Steam ? "from Steam with Steam Input" : "outside Steam (or without Steam Input)").Append('\n');
            sb.Append("Detected from: ").Append(details).Append('\n');
            if (outcome.Length > 0) sb.Append("Controller: ").Append(outcome).Append('\n');
            sb.Append(settingsOnly ? "Opened the emulator without the game: " : "Command: ").Append(command).Append('\n');
            ControllerSwitch.WriteFile(Path.Combine(dataDir, "last start.txt"), new UTF8Encoding(false).GetBytes(sb.ToString()));
        }
        catch (Exception)
        {
            // il registro non è importante: non deve mai impedire di giocare
        }
    }
}
