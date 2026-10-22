// =====================================================================================
//  UnixPlatform.cs
//  Le risposte di IPlatform su Linux e macOS (tutti e due sono sistemi "Unix": cartelle con "/",
//  programmi avviati da riga di comando, nessun registro di Windows).
//    Steam:    Linux ~/.local/share/Steam (o ~/.steam/steam, o la versione Flatpak);
//              macOS ~/Library/Application Support/Steam. Chiusura con il link steam://exit.
//    Link:     Linux xdg-open, macOS open.
//    Cestino:  Linux la cartella Trash standard (freedesktop.org), macOS ~/.Trash.
//    Segreti:  non c'è DPAPI: cifriamo con una chiave ricavata da questo computer e da questo
//              utente, e il file delle impostazioni è leggibile solo da te (permessi 600). Non è
//              una cassaforte, ma la chiave non sta mai in chiaro nel file.
// =====================================================================================

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace SteamAll.Core.Platform;

public sealed class UnixPlatform(OsKind os) : IPlatform
{
    public OsKind Os { get; } = os;

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // ---------------------------------------------------------------------------------
    //  Steam
    // ---------------------------------------------------------------------------------

    public string? FindSteamFolder()
    {
        // Le cartelle possibili, nell'ordine; la prima che contiene "userdata" è quella giusta.
        string[] candidates = Os == OsKind.MacOS
            ? [Path.Combine(Home, "Library", "Application Support", "Steam")]
            : [Path.Combine(Home, ".local", "share", "Steam"),
               Path.Combine(Home, ".steam", "steam"),
               Path.Combine(Home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam")];   // Flatpak
        return candidates.FirstOrDefault(c => Directory.Exists(Path.Combine(c, "userdata")));
    }

    public bool IsSteamRunning()
    {
        string name = Os == OsKind.MacOS ? "steam_osx" : "steam";
        Process[] processes = Process.GetProcessesByName(name);
        foreach (Process p in processes) p.Dispose();
        return processes.Length > 0;
    }

    public async Task<bool> ShutdownSteamAsync(TimeSpan timeout)
    {
        if (!IsSteamRunning()) return true;
        // steam://exit: lo stesso "Esci" del menu di Steam, chiesto con un link.
        OpenLink("steam://exit");
        var clock = Stopwatch.StartNew();
        while (IsSteamRunning())
        {
            if (clock.Elapsed > timeout) return false;
            await Task.Delay(500);
        }
        await Task.Delay(1500);   // un attimo in più: Steam finisce di scrivere i suoi file
        return true;
    }

    public void StartSteam()
    {
        if (Os == OsKind.MacOS) Run("open", "-a Steam");
        else OpenLink("steam://open/main");   // apre Steam (lo avvia se è chiuso)
    }

    // ---------------------------------------------------------------------------------
    //  Link
    // ---------------------------------------------------------------------------------

    public bool IsProtocolRegistered(string protocol) => false;   // vedi l'interfaccia: qui non si usa

    public string? ProtocolExe(string protocol) => null;

    public (string Exe, string ArgsPrefix) LinkOpener => Os == OsKind.MacOS ? ("/usr/bin/open", "") : ("/usr/bin/xdg-open", "");

    private void OpenLink(string link) => Run(LinkOpener.Exe, link);

    private static void Run(string exe, string args)
    {
        try { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { /* il programma non c'è: niente da fare */ }
    }

    // ---------------------------------------------------------------------------------
    //  Cestino
    // ---------------------------------------------------------------------------------

    public void MoveToRecycleBin(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        string name = Path.GetFileName(path.TrimEnd('/'));
        if (Os == OsKind.MacOS)
        {
            string trash = Path.Combine(Home, ".Trash");
            Directory.CreateDirectory(trash);
            Move(path, UniquePath(trash, name));
            return;
        }

        // Linux, standard freedesktop.org: il file va in Trash/files, e accanto in Trash/info un
        // piccolo file .trashinfo dice da dove veniva (così "Ripristina" lo rimette al suo posto).
        string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x : Path.Combine(Home, ".local", "share");
        string files = Path.Combine(dataHome, "Trash", "files");
        string info = Path.Combine(dataHome, "Trash", "info");
        Directory.CreateDirectory(files);
        Directory.CreateDirectory(info);
        string target = UniquePath(files, name);
        File.WriteAllText(Path.Combine(info, Path.GetFileName(target) + ".trashinfo"),
            $"[Trash Info]\nPath={Uri.EscapeDataString(Path.GetFullPath(path)).Replace("%2F", "/")}\nDeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}\n");
        Move(path, target);
    }

    private static string UniquePath(string folder, string name)
    {
        string target = Path.Combine(folder, name);
        for (int i = 2; File.Exists(target) || Directory.Exists(target); i++)
            target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} {i}{Path.GetExtension(name)}");
        return target;
    }

    private static void Move(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to);
    }

    // ---------------------------------------------------------------------------------
    //  Segreti: AES-GCM con una chiave ricavata da questo computer e da questo utente.
    //  AES-GCM cifra e insieme controlla che i dati non siano stati cambiati. Il risultato è:
    //  12 byte "nonce" (un numero casuale, diverso ogni volta) + 16 byte di controllo + i dati cifrati.
    // ---------------------------------------------------------------------------------

    private static byte[] Key()
    {
        string machine = "";
        foreach (string file in (string[])["/etc/machine-id", "/var/lib/dbus/machine-id"])
            if (File.Exists(file)) { machine = File.ReadAllText(file).Trim(); break; }
        if (machine.Length == 0) machine = Environment.MachineName;
        return SHA256.HashData(Encoding.UTF8.GetBytes($"SteamAll.Secret|{machine}|{Environment.UserName}"));
    }

    public byte[] ProtectSecret(byte[] plain)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using var aes = new AesGcm(Key(), tagSizeInBytes: 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return [.. nonce, .. tag, .. cipher];   // ".." = "spread": tutti gli elementi uno dopo l'altro
    }

    public byte[] UnprotectSecret(byte[] protectedData)
    {
        byte[] nonce = protectedData[..12], tag = protectedData[12..28], cipher = protectedData[28..];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(Key(), tagSizeInBytes: 16);
        aes.Decrypt(nonce, cipher, tag, plain);   // errore se la chiave non è quella giusta
        return plain;
    }
}
