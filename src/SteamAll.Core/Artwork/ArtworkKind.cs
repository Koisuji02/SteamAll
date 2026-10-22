// =====================================================================================
//  ArtworkKind.cs
//  I cinque tipi di immagine che Steam usa per un gioco non Steam, e come si chiamano i file
//  nella cartella userdata\<utente>\config\grid (con <appid> l'app id del gioco):
//    Cover   <appid>p.png       copertina verticale 600x900 (la griglia della libreria)
//    Banner  <appid>.png        banner orizzontale 920x430 (vista "recenti", Big Picture)
//    Hero    <appid>_hero.png   l'immagine grande in cima alla pagina del gioco
//    Logo    <appid>_logo.png   il logo sopra l'hero
//    Icon    <appid>-icon.png   l'icona piccola (va anche nel campo "icon" della voce)
// =====================================================================================

namespace SteamAll.Core.Artwork;

public enum ArtworkKind { Cover, Banner, Hero, Logo, Icon }

public static class ArtworkFiles
{
    /// <summary>Il nome del file (senza estensione) di un tipo di immagine.</summary>
    public static string BaseName(uint appId, ArtworkKind kind) => kind switch
    {
        ArtworkKind.Cover => $"{appId}p",
        ArtworkKind.Banner => $"{appId}",
        ArtworkKind.Hero => $"{appId}_hero",
        ArtworkKind.Logo => $"{appId}_logo",
        _ => $"{appId}-icon",
    };

    /// <summary>Il tipo di immagine nello store (le chiavi di Game.StoreImages), per il ripiego.</summary>
    public static string StoreKey(ArtworkKind kind) => kind switch
    {
        ArtworkKind.Cover => "p",
        ArtworkKind.Banner => "wide",
        ArtworkKind.Hero => "hero",
        ArtworkKind.Logo => "logo",
        _ => "icon",
    };

    private static readonly string[] Extensions = ["png", "jpg", "jpeg", "webp", "ico"];

    /// <summary>Il file di quel tipo già presente in grid (non vuoto), o null.</summary>
    public static string? Find(string gridFolder, uint appId, ArtworkKind kind)
    {
        string baseName = BaseName(appId, kind);
        foreach (string ext in Extensions)
        {
            string file = Path.Combine(gridFolder, $"{baseName}.{ext}");
            if (File.Exists(file) && new FileInfo(file).Length > 0) return file;
        }
        return null;
    }

    /// <summary>
    /// Il formato di un'immagine dai suoi primi byte (la "firma"): Steam accetta PNG, JPEG e ICO.
    /// null se è un altro formato (per esempio WebP, che Steam non legge come immagine personalizzata).
    /// </summary>
    public static string? Extension(byte[] data)
    {
        if (data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return "png";
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8) return "jpg";
        if (data.Length >= 4 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0) return "ico";
        return null;
    }

    /// <summary>Larghezza e altezza di un'immagine PNG o JPEG leggendo solo l'intestazione, o null.</summary>
    public static (int Width, int Height)? Size(byte[] d)
    {
        // PNG: larghezza e altezza sono 4 byte ciascuna, "big endian", ai byte 16 e 20.
        if (d.Length >= 24 && d[0] == 0x89 && d[1] == 0x50)
            return (d[16] << 24 | d[17] << 16 | d[18] << 8 | d[19], d[20] << 24 | d[21] << 16 | d[22] << 8 | d[23]);
        // JPEG: si scorrono i "segmenti" fino a quello con le dimensioni (SOFn).
        if (d.Length >= 4 && d[0] == 0xFF && d[1] == 0xD8)
        {
            int i = 2;
            while (i + 9 < d.Length)
            {
                if (d[i] != 0xFF) { i++; continue; }
                int marker = d[i + 1];
                if (marker == 0xFF) { i++; continue; }
                if (marker == 0x01 || marker is >= 0xD0 and <= 0xD9) { i += 2; continue; }
                if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
                    return ((d[i + 7] << 8) | d[i + 8], (d[i + 5] << 8) | d[i + 6]);
                i += 2 + ((d[i + 2] << 8) | d[i + 3]);
            }
        }
        return null;
    }
}
