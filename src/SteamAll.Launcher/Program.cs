// =====================================================================================
//  Program.cs (launcher)
//  Il launcher dei giochi emulati, quello che Steam avvia. SteamAll lo copia:
//    - nella cartella di un gioco "emulatore portable + gioco", come "<Nome del gioco>.exe";
//    - nella cartella del launcher di un emulatore installato (%LOCALAPPDATA%\SteamAll\launchers\<id>),
//      uno per emulatore: lì il gioco da aprire glielo passa Steam, nelle opzioni di avvio.
//  Cosa fa:
//    1. legge <cartella del launcher>\SteamAll\launcher.ini (emulatore, gioco, parametri, controller);
//    2. mette la configurazione del controller giusta (da Steam o fuori da Steam: ControllerSwitch);
//    3. avvia l'emulatore con il gioco e resta aperto finché il gioco gira, così Steam lo vede in
//       esecuzione; se Steam lo chiude ("Esci dal gioco"), Windows chiude anche l'emulatore;
//    4. tenendo premuto Maiusc (Shift) mentre lo avvii, apre l'emulatore SENZA il gioco, per
//       cambiarne le impostazioni.
//  È un programma .NET Framework 4.8 (già dentro Windows): parte all'istante e pesa pochi KB.
// =====================================================================================

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace SteamAll.Launcher;

internal static class Program
{
    private const int VkShift = 0x10;

    [STAThread]
    private static int Main(string[] args)
    {
        // La cartella del launcher = quella dove sta questo exe. I percorsi relativi del file
        // launcher.ini partono da qui.
        string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string title = Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);

        Dictionary<string, string> config = LauncherConfig.Read(Path.Combine(dir, ControllerSwitch.DataFolder, LauncherConfig.FileName));
        string exe = LauncherConfig.Resolve(dir, LauncherConfig.Value(config, "Emulator"));
        if (exe.Length == 0)
            return Fail(title, "This launcher has no settings yet.\nOpen SteamAll and press \"Apply to Steam\".");

        // GetAsyncKeyState: lo stato di un tasto adesso; il bit 0x8000 acceso = è premuto.
        bool settingsOnly = (GetAsyncKeyState(VkShift) & 0x8000) != 0;
        string rom = LauncherConfig.Resolve(dir, LauncherConfig.Value(config, "Game"));
        foreach (string a in args)
        {
            if (string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)) settingsOnly = true;
            else if (rom.Length == 0 && !a.StartsWith("--")) rom = Path.GetFullPath(a);   // il gioco, dalle opzioni di avvio di Steam
        }
        if (rom.Length == 0) settingsOnly = true;   // nessun gioco: si apre l'emulatore

        if (!File.Exists(exe)) return Fail(title, "Emulator not found:\n" + exe + "\n\nOpen SteamAll and check the emulator's path.");
        if (!settingsOnly && !File.Exists(rom)) return Fail(title, "Game file not found:\n" + rom + "\n\nOpen SteamAll and press \"Apply to Steam\".");

        // Il controller: non blocca mai il gioco. Se qualcosa va storto, parte con quello che c'è.
        string details;
        string mode = ControllerSwitch.DetectMode(Environment.GetEnvironmentVariable, out details);
        string controller = LauncherConfig.Value(config, "Controller");
        string dataDir = LauncherConfig.Resolve(dir, LauncherConfig.Value(config, "DataDir"));
        if (dataDir.Length == 0) dataDir = Path.Combine(dir, ControllerSwitch.DataFolder);
        string outcome = "";
        if (controller.Length > 0)
        {
            try { outcome = ControllerSwitch.Prepare(LauncherConfig.Resolve(dir, LauncherConfig.Value(config, "ConfigDir")), dataDir, controller, mode); }
            catch (Exception ex) { outcome = "not prepared (" + ex.Message + ")"; }
        }

        // La riga di comando: "emulatore" e i parametri, con il gioco al posto di {rom}.
        var command = new StringBuilder();
        command.Append('"').Append(exe).Append('"');
        if (!settingsOnly) command.Append(' ').Append(LauncherConfig.CommandLine(LauncherConfig.Arguments(config), rom));
        WriteLastStart(dataDir, mode, details, outcome, settingsOnly, command.ToString());

        return Run(title, exe, command);
    }

    // Avvia l'emulatore dentro un "job object" di Windows: un gruppo di processi che, quando il
    // launcher viene chiuso (anche a forza, da Steam), Windows chiude tutti insieme.
    private static int Run(string title, string exe, StringBuilder command)
    {
        IntPtr job = Native.CreateKillOnCloseJob();
        var si = new Native.STARTUPINFO { cb = Marshal.SizeOf(typeof(Native.STARTUPINFO)) };
        Native.PROCESS_INFORMATION pi;
        if (!Native.CreateProcess(exe, command, IntPtr.Zero, IntPtr.Zero, false, Native.CreateSuspended, IntPtr.Zero,
                                  Path.GetDirectoryName(exe), ref si, out pi))
            return Fail(title, "Can't start the emulator (error " + Marshal.GetLastWin32Error() + "):\n" + command);

        // Il processo nasce "sospeso": lo mettiamo nel gruppo PRIMA che parta, poi lo facciamo partire.
        if (job != IntPtr.Zero && !Native.AssignProcessToJobObject(job, pi.hProcess))
        {
            Native.CloseHandle(job);
            job = IntPtr.Zero;
        }
        Native.AllowSetForegroundWindow(pi.dwProcessId);
        Native.ResumeThread(pi.hThread);
        Native.CloseHandle(pi.hThread);

        // Da qui in poi aspettiamo e basta: restituiamo a Windows la memoria che non ci serve.
        Native.SetProcessWorkingSetSize(Native.GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
        Native.WaitForSingleObject(pi.hProcess, Native.Infinite);
        uint exitCode;
        if (!Native.GetExitCodeProcess(pi.hProcess, out exitCode)) exitCode = 0;
        Native.CloseHandle(pi.hProcess);

        // Uscita normale: lasciamo stare eventuali programmi che l'emulatore ha avviato da sé (es. un updater).
        if (job != IntPtr.Zero)
        {
            Native.SetKillOnClose(job, false);
            Native.CloseHandle(job);
        }
        return (int)exitCode;
    }

    private static int Fail(string title, string message)
    {
        // MB_ICONERROR | MB_TOPMOST | MB_SETFOREGROUND: visibile anche sopra Steam Big Picture.
        Native.MessageBox(IntPtr.Zero, message, title, 0x10 | 0x40000 | 0x10000);
        return 1;
    }

    // <dati>\last start.txt: com'è andato l'ultimo avvio (utile se qualcosa non torna).
    private static void WriteLastStart(string dataDir, string mode, string details, string outcome, bool settingsOnly, string command)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("SteamAll - last start of this game: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("\r\n");
            sb.Append("Started: ").Append(mode == ControllerSwitch.Steam ? "from Steam with Steam Input" : "outside Steam (or without Steam Input)").Append("\r\n");
            sb.Append("Detected from: ").Append(details).Append("\r\n");
            if (outcome.Length > 0) sb.Append("Controller: ").Append(outcome).Append("\r\n");
            sb.Append(settingsOnly ? "Opened the emulator without the game (Shift held): " : "Command: ").Append(command).Append("\r\n");
            ControllerSwitch.WriteFile(Path.Combine(dataDir, "last start.txt"), new UTF8Encoding(false).GetBytes(sb.ToString()));
        }
        catch (Exception)
        {
            // il registro non è importante: non deve mai impedire di giocare
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
