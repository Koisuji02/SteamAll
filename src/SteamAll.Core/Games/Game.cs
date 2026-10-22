// =====================================================================================
//  Game.cs
//  Un gioco trovato da una fonte, così come la fonte lo descrive. Le fonti sono tre:
//    - una cartella dei giochi (una sottocartella per gioco: emulatore portable + gioco, o gioco PC);
//    - un emulatore installato con le sue cartelle di ROM (come Steam ROM Manager);
//    - uno store (GOG GALAXY, Epic...), con i giochi installati E quelli solo posseduti.
//  Non dice ancora niente di Steam: il confronto con la libreria di Steam e lo stato (New,
//  In Steam...) li fa il piano (ImportPlan), che usa questi dati.
// =====================================================================================

namespace SteamAll.Core.Games;

/// <summary>Da quale tipo di fonte viene il gioco.</summary>
public enum GameOrigin { Folder, Emulator, Store }

// "record": una classe pensata per contenere dati. Qui le proprietà sono "init": si scrivono
// solo quando l'oggetto viene creato, poi restano fisse. Se serve una versione modificata si
// usa "with":  var copia = gioco with { Problem = "..." };  (crea un record nuovo, l'originale resta).
public sealed record Game
{
    // "required" = chi crea un Game DEVE dare un valore a questa proprietà: se se ne dimentica,
    // il compilatore dà errore. Le altre hanno un valore predefinito dopo "=".

    /// <summary>Identificativo unico e stabile, es. "folder:c:\games\kirby", "epic:&lt;id&gt;", "gog_1207", "rom:...".</summary>
    public required string Key { get; init; }

    /// <summary>Il nome che avrà in Steam.</summary>
    public required string Name { get; init; }

    /// <summary>La fonte da mostrare: il percorso della cartella, il nome dell'emulatore o dello store.</summary>
    public required string Source { get; init; }

    public GameOrigin Origin { get; init; } = GameOrigin.Folder;

    /// <summary>Solo giochi degli store: lo store a cui appartiene ("gog", "epic", "ubisoft"...). Vedi StoreCatalog.</summary>
    public string StoreId { get; init; } = "";

    /// <summary>
    /// Solo giochi degli store: quale store l'ha letto. Di solito è lo stesso di StoreId, ma GOG GALAXY
    /// legge anche i giochi di Ubisoft, EA... che gli hai collegato: per quelli StoreId = "ubisoft",
    /// ProviderId = "gog" (li avvia e li installa GALAXY).
    /// </summary>
    public string ProviderId { get; init; } = "";

    /// <summary>Il tipo: "Ryujinx (Switch)", "PC game", "GOG", "Ubisoft", "Epic", "GameCube"...</summary>
    public string Kind { get; init; } = "";

    /// <summary>false = gioco di uno store, posseduto ma non installato: in Steam diventa "da installare".</summary>
    public bool IsInstalled { get; init; } = true;

    public string InstallFolder { get; init; } = "";   // la cartella del gioco installato
    public string ExePath { get; init; } = "";         // l'exe che Steam avvia (senza virgolette)
    public string StartDir { get; init; } = "";        // la cartella di avvio
    public string LaunchOptions { get; init; } = "";   // le opzioni di avvio (per le ROM: il file del gioco)
    public string StoreUri { get; init; } = "";        // il link che apre lo store sul gioco
    public string EpicAppName { get; init; } = "";     // solo Epic: il nome interno del gioco
    public string Icon { get; init; } = "";            // un'icona già sul disco, se la fonte ne ha una

    /// <summary>L'emulatore, per i giochi emulati (null per gli altri).</summary>
    public Emulator? Emulator { get; init; }

    /// <summary>Giochi emulati: il programma dell'emulatore (percorso completo).</summary>
    public string EmulatorExe { get; init; } = "";

    /// <summary>Solo emulatori installati: quale configurazione di emulatore (EmulatorSetup.Id).</summary>
    public string EmulatorSetupId { get; init; } = "";

    /// <summary>
    /// Giochi emulati: il file del gioco (rom, iso...). Nelle cartelle dei giochi è relativo alla
    /// cartella del gioco; per gli emulatori installati è il percorso completo.
    /// </summary>
    public string RomPath { get; init; } = "";

    /// <summary>Immagini dello store per tipo ("p" copertina, "hero", "logo", "icon", "wide").</summary>
    // IReadOnlyDictionary = un dizionario che chi lo riceve può solo leggere.
    public IReadOnlyDictionary<string, string> StoreImages { get; init; } = new Dictionary<string, string>();

    /// <summary>Se non è null, c'è un problema e il gioco va saltato: il testo spiega perché.</summary>
    public string? Problem { get; init; }

    /// <summary>Un'informazione in più da mostrare (es. "Launcher will be created").</summary>
    public string Note { get; init; } = "";
}
