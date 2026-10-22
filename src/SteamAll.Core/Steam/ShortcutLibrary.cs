// =====================================================================================
//  ShortcutLibrary.cs
//  I giochi non Steam di un utente: il suo shortcuts.vdf letto, e le sue immagini (grid).
//  Per ora solo lettura; nella Fase 2 aggiungeremo il salvataggio (con Steam chiuso).
// =====================================================================================

namespace SteamAll.Core.Steam;

public sealed class ShortcutLibrary
{
    public SteamUser User { get; }
    public string FilePath => Path.Combine(User.ConfigFolder, "shortcuts.vdf");
    public string GridFolder => Path.Combine(User.ConfigFolder, "grid");

    /// <summary>Tutto il file letto: servirà per riscriverlo.</summary>
    public VdfNode Root { get; }

    /// <summary>La lista "shortcuts" dentro il file: una voce per gioco.</summary>
    public VdfNode List { get; }

    /// <summary>I giochi non Steam, uno per voce della lista.</summary>
    public List<Shortcut> Shortcuts { get; }

    // "private": il costruttore si usa solo da dentro la classe; da fuori si usa Load (sotto).
    private ShortcutLibrary(SteamUser user, VdfNode root)
    {
        User = user;
        Root = root;
        List = root.GetNode("shortcuts")
            ?? throw new InvalidDataException($"{FilePath} has no shortcuts list");   // "?? throw": errore se manca
        // Select trasforma ogni voce nel suo Shortcut; Where tiene solo le voci che sono mappe.
        Shortcuts = List.Entries.Where(e => e.Child is not null).Select(e => new Shortcut(e.Child!)).ToList();
    }

    /// <summary>Legge shortcuts.vdf dell'utente (se non c'è, una libreria vuota: è normale).</summary>
    public static ShortcutLibrary Load(SteamUser user)
    {
        string file = Path.Combine(user.ConfigFolder, "shortcuts.vdf");
        VdfNode root = File.Exists(file) ? BinaryVdf.Read(Files.ReadShared(file)) : EmptyLibrary();
        return new ShortcutLibrary(user, root);
    }

    /// <summary>
    /// Salva shortcuts.vdf (Steam DEVE essere chiuso: altrimenti alla sua chiusura lo riscrive lui).
    /// Il file di prima resta accanto come shortcuts.vdf.steamall-backup.
    /// </summary>
    public void Save()
    {
        // Le voci della lista si chiamano "0", "1", "2"...: dopo aggiunte e tolte, si rinumerano come fa Steam.
        for (int i = 0; i < List.Entries.Count; i++) List.Entries[i].Key = i.ToString();

        Directory.CreateDirectory(User.ConfigFolder);
        string temp = FilePath + ".steamall.tmp";
        File.WriteAllBytes(temp, BinaryVdf.Write(Root));
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".steamall-backup");   // Replace tiene la copia di prima
        else File.Move(temp, FilePath);
    }

    /// <summary>La voce con questo app id (null se non c'è).</summary>
    public VdfEntry? FindEntry(uint appId) =>
        List.Entries.FirstOrDefault(e => e.Child is not null && new Shortcut(e.Child).AppId == appId);

    /// <summary>Una libreria vuota: { "shortcuts" { } }</summary>
    public static VdfNode EmptyLibrary()
    {
        var root = new VdfNode();
        root.Entries.Add(VdfEntry.FromMap("shortcuts", new VdfNode()));
        return root;
    }

    // I formati di immagine che Steam accetta in grid. "static readonly": un elenco solo, fisso.
    private static readonly string[] ImageExtensions = ["png", "jpg", "jpeg", "webp"];

    /// <summary>
    /// Un'immagine già presente in grid (non vuota), o null. kind: "p" = copertina 600x900,
    /// "" = banner 920x430, "_hero" = sfondo, "_logo" = logo.
    /// </summary>
    public string? FindImage(uint appId, string kind)
    {
        foreach (string ext in ImageExtensions)
        {
            string file = Path.Combine(GridFolder, $"{appId}{kind}.{ext}");
            if (File.Exists(file) && new FileInfo(file).Length > 0) return file;
        }
        return null;
    }
}
