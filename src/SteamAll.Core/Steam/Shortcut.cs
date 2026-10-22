// =====================================================================================
//  Shortcut.cs
//  Un gioco non Steam già presente in Steam: una voce di shortcuts.vdf, vista "da comodi".
//  Non copia i dati: ogni proprietà li legge dalla voce vera (Node), così è sempre aggiornata
//  e, quando nella Fase 2 la modificheremo, cambierà direttamente quello che verrà riscritto.
// =====================================================================================

using System.Text.RegularExpressions;

namespace SteamAll.Core.Steam;

// "(VdfNode node)" dopo il nome della classe è un "costruttore primario": il parametro node si
// passa con new Shortcut(nodo) ed è usabile in tutta la classe.
public sealed class Shortcut(VdfNode node)
{
    public VdfNode Node { get; } = node;

    // Proprietà "calcolate" con "=>": non hanno un valore proprio, lo leggono dalla voce ogni volta.
    // uint.TryParse prova a leggere il numero: se riesce lo mette in "id" (dichiarata lì, "out var").
    public uint AppId => uint.TryParse(Node.GetText("appid"), out var id) ? id : 0;
    public string Name => Node.GetText("AppName");
    public string Exe => Node.GetText("Exe");                  // tra virgolette: "C:\...\gioco.exe"
    public string StartDir => Node.GetText("StartDir");
    public string LaunchOptions => Node.GetText("LaunchOptions").Trim();
    public string Icon => Node.GetText("icon");
    public string DevkitGameId => Node.GetText("DevkitGameID");
    public List<string> Tags => Node.GetNode("tags")?.Values() ?? [];

    // L'exe senza virgolette e senza spazi intorno.
    public string ExePath => Exe.Trim().Trim('"');

    // Chi l'ha creata: il "marchio" nel campo DevkitGameID (vedi Markers). StringComparison.Ordinal =
    // confronto esatto, maiuscole comprese (come BoilR).
    public bool IsBoilR => DevkitGameId.StartsWith("boilr", StringComparison.Ordinal) || Tags.Contains("boilr");
    public bool IsPlaceholder => Markers.IsPlaceholder(DevkitGameId);
    public bool IsSteamAll => Markers.IsSteamAll(DevkitGameId);

    /// <summary>Le voci che possiamo aggiornare o togliere. Quelle aggiunte a mano non le tocchiamo mai.</summary>
    public bool IsManaged => IsBoilR || IsPlaceholder || IsSteamAll;

    /// <summary>Il link dello store nelle opzioni di avvio di un segnaposto ("" se non c'è).</summary>
    public string StoreUri
    {
        get
        {
            // Espressione regolare: un nome di link (lettere, cifre, + . -) seguito da "://" e da
            // tutto quello che non è uno spazio o una virgoletta.
            Match m = Regex.Match(LaunchOptions, @"[A-Za-z][A-Za-z0-9+.-]*://[^\s""]+");
            return m.Success ? m.Value : "";
        }
    }

    /// <summary>
    /// Una voce nuova, con i campi nell'ordine in cui li scrive Steam. Servirà nella Fase 2.
    /// </summary>
    public static VdfNode CreateNode(uint appId, string name, string exe, string startDir,
                                     string icon, string launchOptions, string marker, bool allowOverlay)
    {
        var node = new VdfNode();
        // AddRange aggiunge più elementi insieme; tra [ ] c'è l'elenco (una "collection expression").
        node.Entries.AddRange(
        [
            VdfEntry.FromUInt32("appid", appId),
            VdfEntry.FromString("AppName", name),
            VdfEntry.FromString("Exe", exe),
            VdfEntry.FromString("StartDir", startDir),
            VdfEntry.FromString("icon", icon),
            VdfEntry.FromString("ShortcutPath", ""),
            VdfEntry.FromString("LaunchOptions", launchOptions),
            VdfEntry.FromUInt32("IsHidden", 0),
            VdfEntry.FromUInt32("AllowDesktopConfig", 1),
            VdfEntry.FromUInt32("AllowOverlay", allowOverlay ? 1u : 0u),   // 1u = 1 di tipo uint
            VdfEntry.FromUInt32("openvr", 0),
            VdfEntry.FromUInt32("Devkit", 0),
            VdfEntry.FromString("DevkitGameID", marker),
            VdfEntry.FromUInt32("DevkitOverrideAppID", 0),
            VdfEntry.FromUInt32("LastPlayTime", 0),
            VdfEntry.FromString("FlatpakAppID", ""),
            VdfEntry.FromMap("tags", new VdfNode()),
        ]);
        return node;
    }
}
