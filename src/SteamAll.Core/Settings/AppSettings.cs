// =====================================================================================
//  AppSettings.cs
//  Le impostazioni del programma, salvate in un file JSON:
//    Windows: %APPDATA%\SteamAll\settings.json      Linux e macOS: ~/.config/SteamAll/settings.json
//  Dentro ci sono anche i giochi e le cartelle a cui hai tolto la spunta, gli store spenti e gli
//  emulatori installati con le loro cartelle di ROM.
//
//  La chiave API di SteamGridDB NON viene mai scritta in chiaro: la cifra il sistema operativo
//  con il tuo account (su Windows con DPAPI; su Linux e macOS vedi UnixPlatform). Nel file c'è
//  solo la versione cifrata, che su un altro PC o con un altro utente non si può decifrare.
// =====================================================================================

using System.Text;
using System.Text.Json;                    // JsonSerializer: da oggetto a JSON e ritorno
using System.Text.Json.Serialization;      // [JsonIgnore]
using SteamAll.Core.Platform;

namespace SteamAll.Core.Settings;

public sealed class AppSettings
{
    // --- Cosa si salva -------------------------------------------------------------------
    // JsonSerializer salva le proprietà pubbliche con get e set, con il loro nome.
    // I valori dopo "=" sono quelli del primo avvio.

    /// <summary>Le cartelle dei giochi da scansionare (una sottocartella per gioco). Su Windows si parte da C:\Games.</summary>
    public List<string> GameFolders { get; set; } = OperatingSystem.IsWindows() ? [@"C:\Games"] : [];

    /// <summary>Gli emulatori installati nel sistema, con le loro cartelle di ROM.</summary>
    public List<EmulatorSetup> Emulators { get; set; } = [];

    /// <summary>
    /// Gli store spenti (StoreInfo.Id, es. "ea"): i loro giochi non si importano, e quelli già in
    /// Steam si tolgono. Gli store installati sono accesi finché non li spegni.
    /// </summary>
    public HashSet<string> DisabledStores { get; set; } = [];

    /// <summary>
    /// Come usi SteamAll (lo scegli al primo avvio): "installed" = solo i giochi installati (come
    /// BoilR); "all" = anche quelli posseduti e non installati, con il tasto Installa (serve il
    /// plugin per Millennium); "" = non ancora scelto.
    /// </summary>
    public string Mode { get; set; } = "";

    public const string ModeInstalled = "installed";
    public const string ModeAll = "all";

    /// <summary>I giochi degli store non installati vanno in Steam? (Sì finché non scegli "solo installati".)</summary>
    [JsonIgnore]
    public bool IncludeNotInstalled => Mode != ModeInstalled;

    /// <summary>La finestra a schermo intero (ingrandita)? Si apre come l'hai chiusa l'ultima volta; la prima volta sì.</summary>
    public bool WindowMaximized { get; set; } = true;

    /// <summary>La misura della finestra quando non è a schermo intero (pixel "logici").</summary>
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;

    /// <summary>La cartella di userdata da usare; "" = chi ha usato Steam per ultimo.</summary>
    public string SteamUserId { get; set; } = "";

    /// <summary>La chiave API di SteamGridDB, cifrata (in base64). Usa <see cref="GetSteamGridDbKey"/>.</summary>
    public string SteamGridDbKeyProtected { get; set; } = "";

    public bool HeroHd { get; set; } = true;     // hero 3840x1240 da SteamGridDB
    public bool Cleanup { get; set; } = true;    // immagini rimaste nel Cestino

    /// <summary>I launcher cambiano da soli la configurazione del controller (da Steam / fuori da Steam).</summary>
    public bool ManageControllers { get; set; } = true;

    /// <summary>Le chiavi dei giochi a cui hai tolto la spunta (Game.Key).</summary>
    public HashSet<string> ExcludedGames { get; set; } = [];

    /// <summary>
    /// Le immagini che hai scelto per un gioco nella sua pagina di dettaglio: chiave del gioco
    /// (Game.Key) -> tipo ("Cover", "Hero"...) -> indirizzo dell'immagine su SteamGridDB. Le scrive Apply.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> ArtworkChoices { get; set; } = [];

    /// <summary>Le cartelle dei giochi a cui hai tolto la spunta (es. "D:\Giochi").</summary>
    public HashSet<string> ExcludedSources { get; set; } = [];

    // --- La chiave API ---------------------------------------------------------------------
    // Due metodi invece di una proprietà, perché servono la piattaforma (che sa cifrare).

    /// <summary>La chiave in chiaro, solo in memoria ("" se non c'è o non si decifra).</summary>
    public string GetSteamGridDbKey(IPlatform platform)
    {
        if (SteamGridDbKeyProtected.Length == 0) return "";
        try
        {
            byte[] plain = platform.UnprotectSecret(Convert.FromBase64String(SteamGridDbKeyProtected));
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception)   // file copiato da un altro PC o rovinato: la chiave va reinserita
        {
            return "";
        }
    }

    public void SetSteamGridDbKey(IPlatform platform, string key)
    {
        key = key.Trim();
        SteamGridDbKeyProtected = key.Length == 0
            ? ""
            : Convert.ToBase64String(platform.ProtectSecret(Encoding.UTF8.GetBytes(key)));
    }

    // --- Lettura e scrittura del file -----------------------------------------------------

    // Le opzioni del JSON: rientri per renderlo leggibile se lo apri con un editor.
    // "static readonly" = un solo oggetto per tutto il programma.
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Il file predefinito delle impostazioni.</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SteamAll", "settings.json");

    /// <summary>Legge le impostazioni; al primo avvio (o se il file è rovinato) quelle predefinite.</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // "when" = questo catch vale solo per questi errori. File rovinato o illeggibile:
            // si riparte dai valori predefiniti (al primo salvataggio il file si riscrive).
        }
        return new AppSettings();
    }

    /// <summary>
    /// Salva in modo sicuro: prima un file accanto, poi lo mette al posto del vecchio. Se il PC si
    /// spegne a metà, resta intero uno dei due.
    /// </summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        // Su Linux e macOS: il file lo può leggere e scrivere solo il tuo utente (permessi "600").
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, path, overwrite: true);
    }
}
