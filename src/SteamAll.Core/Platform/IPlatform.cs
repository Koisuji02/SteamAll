// =====================================================================================
//  IPlatform.cs
//  Le cose che cambiano da un sistema operativo all'altro, raccolte in un'"interfaccia".
//
//  Un'interfaccia è un elenco di metodi SENZA il codice: dice "cosa" si può chiedere, non "come".
//  Il "come" lo scrive una classe per ogni sistema: WindowsPlatform e UnixPlatform (Linux e
//  macOS). Il resto del programma chiede le cose a IPlatform e non sa (né deve sapere) quale
//  sistema c'è sotto. Anche le prove ne approfittano: usano una piattaforma "finta" che non
//  tocca lo Steam vero. (Per convenzione in .NET i nomi delle interfacce iniziano con la I.)
// =====================================================================================

namespace SteamAll.Core.Platform;

/// <summary>I sistemi operativi su cui gira SteamAll.</summary>
public enum OsKind { Windows, Linux, MacOS }

public interface IPlatform
{
    /// <summary>Su quale sistema siamo.</summary>
    OsKind Os { get; }

    // --- Steam ------------------------------------------------------------------------

    /// <summary>La cartella dove è installato Steam, o null se Steam non c'è.</summary>
    // "string?" = un testo che può anche valere null (vedi Nullable in Directory.Build.props).
    string? FindSteamFolder();

    /// <summary>true se Steam è aperto.</summary>
    bool IsSteamRunning();

    /// <summary>
    /// Chiude Steam subito (come "Esci" dal suo menu) e aspetta che sia chiuso davvero.
    /// "Task&lt;bool&gt;" = metodo asincrono che alla fine dice se ci è riuscito entro "timeout".
    /// </summary>
    Task<bool> ShutdownSteamAsync(TimeSpan timeout);

    /// <summary>Apre Steam.</summary>
    void StartSteam();

    // --- Store e link -------------------------------------------------------------------

    /// <summary>
    /// true se sul PC c'è un programma che apre i link di questo tipo (es. "goggalaxy" per
    /// goggalaxy://...): vuol dire che quello store è installato. (Su Linux e macOS non si può
    /// chiedere in modo affidabile: gli store lì si riconoscono dalle loro cartelle.)
    /// </summary>
    bool IsProtocolRegistered(string protocol);

    /// <summary>L'exe che apre i link di quel tipo (es. EpicGamesLauncher.exe), o null.</summary>
    string? ProtocolExe(string protocol);

    /// <summary>
    /// Come aprire un link "come un clic": il programma e l'inizio delle opzioni.
    /// Windows: rundll32.exe "url.dll,FileProtocolHandler " (esce subito, quindi Steam non pensa
    /// che il "gioco" sia in esecuzione; explorer.exe NON va bene: il link di Epic apre Documenti).
    /// Linux: xdg-open. macOS: open.
    /// </summary>
    (string Exe, string ArgsPrefix) LinkOpener { get; }
    // "(string Exe, string ArgsPrefix)" = una "tupla": due valori con un nome, restituiti insieme.

    // --- File e segreti -----------------------------------------------------------------

    /// <summary>Sposta un file (o una cartella) nel Cestino: mai cancellazioni definitive dei file dell'utente.</summary>
    void MoveToRecycleBin(string path);

    /// <summary>Cifra un segreto (la chiave API) in modo che solo questo utente, su questo PC, lo possa leggere.</summary>
    byte[] ProtectSecret(byte[] plain);

    /// <summary>Il contrario di ProtectSecret. Dà errore se il segreto viene da un altro PC o utente.</summary>
    byte[] UnprotectSecret(byte[] protectedData);
}

// Il sistema su cui stiamo girando. "static class": c'è una sola "piattaforma attuale" per tutto il programma.
public static class Platforms
{
    // "condizione ? a : b" sceglie tra due valori. OperatingSystem.IsWindows()/IsMacOS() dicono dove siamo.
    public static IPlatform Current { get; } =
        OperatingSystem.IsWindows() ? new WindowsPlatform()
        : OperatingSystem.IsMacOS() ? new UnixPlatform(OsKind.MacOS)
        : new UnixPlatform(OsKind.Linux);
}
