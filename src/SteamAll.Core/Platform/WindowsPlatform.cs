// =====================================================================================
//  WindowsPlatform.cs
//  Le risposte di IPlatform su Windows: registro di Windows, processi, Cestino, DPAPI.
// =====================================================================================

using System.Diagnostics;                 // Process: i programmi in esecuzione
using System.Runtime.InteropServices;     // DllImport, StructLayout (per il Cestino)
using System.Runtime.Versioning;          // SupportedOSPlatform
using System.Security.Cryptography;       // ProtectedData (pacchetto System.Security.Cryptography.ProtectedData)
using System.Text.RegularExpressions;
using Microsoft.Win32;                    // Registry: il registro di Windows

namespace SteamAll.Core.Platform;

// [SupportedOSPlatform("windows")] dice al compilatore che questa classe funziona solo su Windows.
// "sealed" = nessuno può ereditare da questa classe. ": IPlatform" = la classe "implementa"
// l'interfaccia: deve scrivere tutti i suoi metodi.
[SupportedOSPlatform("windows")]
public sealed class WindowsPlatform : IPlatform
{
    public OsKind Os => OsKind.Windows;

    // ---------------------------------------------------------------------------------
    //  Steam
    // ---------------------------------------------------------------------------------

    public string? FindSteamFolder()
    {
        // Steam scrive dove si trova nel registro: HKEY_CURRENT_USER\Software\Valve\Steam, valore SteamPath.
        // "using var" chiude la chiave alla fine del metodo; "?." chiama GetValue solo se la chiave c'è.
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        string? path = key?.GetValue("SteamPath") as string;

        // Se il registro non lo dice, proviamo la cartella predefinita. "??=" assegna solo se è null.
        path ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");

        // Nel registro Steam scrive le barre al contrario ("C:/Program Files (x86)/Steam"): le giriamo.
        path = path.Replace('/', '\\');
        return Directory.Exists(path) ? path : null;
    }

    // steam.exe: dal registro (SteamExe), altrimenti nella cartella di Steam.
    private string? FindSteamExe()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (key?.GetValue("SteamExe") is string exe && File.Exists(exe.Replace('/', '\\'))) return exe.Replace('/', '\\');
        string? folder = FindSteamFolder();
        string fallback = folder is null ? "" : Path.Combine(folder, "steam.exe");
        return File.Exists(fallback) ? fallback : null;
    }

    public bool IsSteamRunning()
    {
        // GetProcessesByName dà i processi con quel nome (senza ".exe"). Li "liberiamo" con Dispose.
        Process[] processes = Process.GetProcessesByName("steam");
        foreach (Process p in processes) p.Dispose();
        return processes.Length > 0;
    }

    public async Task<bool> ShutdownSteamAsync(TimeSpan timeout)
    {
        if (!IsSteamRunning()) return true;
        string? exe = FindSteamExe();
        if (exe is null) return false;

        // "steam.exe -shutdown" chiede a Steam di chiudersi da solo, come "Steam > Esci": salva i
        // suoi dati per bene (al contrario di "uccidere" il processo, che potrebbe rovinarli).
        Process.Start(new ProcessStartInfo(exe, "-shutdown") { UseShellExecute = false })?.Dispose();

        // Aspettiamo che sparisca, controllando ogni mezzo secondo. Stopwatch = un cronometro.
        var clock = Stopwatch.StartNew();
        while (IsSteamRunning())
        {
            if (clock.Elapsed > timeout) return false;
            await Task.Delay(500);   // "await Task.Delay": aspetta senza bloccare l'interfaccia
        }
        await Task.Delay(1500);      // un attimo in più: Steam finisce di scrivere i suoi file
        return true;
    }

    public void StartSteam()
    {
        string? exe = FindSteamExe();
        if (exe is not null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true })?.Dispose();
    }

    // ---------------------------------------------------------------------------------
    //  Link degli store
    // ---------------------------------------------------------------------------------

    public bool IsProtocolRegistered(string protocol) => ProtocolCommand(protocol) is not null;

    public string? ProtocolExe(string protocol)
    {
        // Il comando è tipo:  "C:\...\EpicGamesLauncher.exe" %1   oppure   C:\...\GalaxyClient.exe "%1"
        // L'espressione regolare prende il primo pezzo: tra virgolette, o fino al primo spazio.
        string? command = ProtocolCommand(protocol);
        if (command is null) return null;
        Match m = Regex.Match(command, @"^\s*(?:""([^""]+)""|(\S+))");
        string exe = m.Groups[1].Value + m.Groups[2].Value;
        return exe.Length > 0 && File.Exists(exe) ? exe : null;
    }

    // Windows sa quale programma apre un tipo di link grazie a una chiave del registro:
    // HKEY_CLASSES_ROOT\<tipo>\shell\open\command (il valore senza nome contiene il comando).
    private static string? ProtocolCommand(string protocol)
    {
        // $@"..." = stringa interpolata ($) e letterale (@) insieme.
        using var key = Registry.ClassesRoot.OpenSubKey($@"{protocol}\shell\open\command");
        return key?.GetValue("") as string;
    }

    public (string Exe, string ArgsPrefix) LinkOpener =>
        (Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\WINDOWS", "System32", "rundll32.exe"),
         "url.dll,FileProtocolHandler ");

    // ---------------------------------------------------------------------------------
    //  Cestino: SHFileOperation di Windows con FOF_ALLOWUNDO = "sposta nel Cestino".
    //  È una funzione in C della shell di Windows: la chiamiamo con P/Invoke.
    // ---------------------------------------------------------------------------------

    public void MoveToRecycleBin(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        var op = new ShFileOperation
        {
            Func = FoDelete,
            From = Path.GetFullPath(path) + "\0\0",   // l'elenco dei file finisce con due caratteri 0
            Flags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi,
        };
        int result = SHFileOperation(ref op);
        if (result != 0 || op.AnyOperationsAborted)
            throw new IOException($"Can't move {Path.GetFileName(path)} to the Recycle Bin (error {result})");
    }

    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004, FofNoConfirmation = 0x0010, FofAllowUndo = 0x0040, FofNoErrorUi = 0x0400;

    // La struttura che SHFileOperation si aspetta, campo per campo come in C.
    // CharSet.Unicode = i testi passano come UTF-16, come vuole la versione "W" della funzione.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOperation
    {
        public IntPtr Window;
        public uint Func;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref ShFileOperation operation);

    // ---------------------------------------------------------------------------------
    //  Segreti: DPAPI ("Data Protection API") di Windows, legata al tuo account.
    //  L'"entropia" è un ingrediente in più: solo chi la conosce (SteamAll) può decifrare.
    // ---------------------------------------------------------------------------------

    private static readonly byte[] Entropy = "SteamAll.Secret"u8.ToArray();   // u8 = testo già in byte UTF-8

    public byte[] ProtectSecret(byte[] plain) =>
        ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

    public byte[] UnprotectSecret(byte[] protectedData) =>
        ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
}
