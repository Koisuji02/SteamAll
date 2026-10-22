// =====================================================================================
//  ShortcutSpec.cs
//  Come deve essere la voce di Steam di un gioco: exe, cartella di avvio, opzioni, icona.
//  LaunchCommands decide questi valori; l'Applier li scrive in shortcuts.vdf.
// =====================================================================================

using SteamAll.Core.Steam;

namespace SteamAll.Core.Apply;

/// <param name="ExePath">L'exe senza virgolette (in Steam viene scritto tra virgolette).</param>
/// <param name="StartDir">La cartella di avvio, senza virgolette.</param>
/// <param name="Marker">Il "marchio" in DevkitGameID: chi ha creato la voce (vedi Shortcut).</param>
public sealed record ShortcutSpec(string Name, string ExePath, string StartDir, string LaunchOptions,
                                  string Icon, string Marker, bool AllowOverlay)
{
    /// <summary>L'app id che Steam darà a una voce nuova con questo exe e questo nome.</summary>
    public uint AppId => SteamAppId.ForShortcut(ExePath, Name);

    /// <summary>Exe e cartella come li scrive Steam: tra virgolette (la cartella senza "\" finale).</summary>
    public string QuotedExe => $"\"{ExePath}\"";
    public string QuotedStartDir => $"\"{StartDir.TrimEnd('\\', '/')}\"";

    /// <summary>La voce nuova da mettere in shortcuts.vdf.</summary>
    public VdfNode ToNode() =>
        Shortcut.CreateNode(AppId, Name, QuotedExe, QuotedStartDir, Icon, LaunchOptions, Marker, AllowOverlay);

    /// <summary>
    /// Aggiorna una voce esistente perché avvii il gioco così. L'app id NON cambia (Steam tiene
    /// quello salvato): immagini, raccolte e tempo di gioco restano. Restituisce true se ha cambiato qualcosa.
    /// </summary>
    public bool UpdateNode(VdfNode node)
    {
        bool changed = false;
        // Una "funzione locale": vive solo dentro questo metodo e può usare le sue variabili.
        void Set(string key, string value)
        {
            if (node.GetText(key) == value) return;
            node.SetText(key, value);
            changed = true;
        }
        Set("Exe", QuotedExe);
        Set("StartDir", QuotedStartDir);
        Set("LaunchOptions", LaunchOptions);
        if (Icon.Length > 0) Set("icon", Icon);
        Set("DevkitGameID", Marker);
        return changed;
    }
}
