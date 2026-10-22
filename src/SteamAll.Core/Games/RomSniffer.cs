// =====================================================================================
//  RomSniffer.cs
//  Alcune estensioni le usano più sistemi: un .iso può essere GameCube, Wii, PS2, PSP...; un .chd
//  PS1 o PS2. Per dare ogni file all'emulatore giusto (anche se tutte le ROM stanno nella stessa
//  cartella) guardiamo i primi byte del file, dove ogni formato ha la sua "firma":
//    GameCube   il numero C2339F3D alla posizione 0x1C
//    Wii        il numero 5D1C9EA3 alla posizione 0x18
//    ISO 9660   (dischi PlayStation): "CD001" a 0x8001 e l'identificativo del sistema a 0x8008:
//               "PLAYSTATION" per PS1 e PS2, "PSP GAME" per la PSP
//    CHD        (dischi compressi): "MComprHD" all'inizio; nei metadati "DVD " = DVD (PS2),
//               "CHT2"/"CHTR"/"CHCD" = CD (di solito PS1)
//  E, come aiuto, i nomi delle cartelle: un file in "PS2" o "PlayStation 2" è della PS2.
//  Si legge solo l'inizio del file, mai lo si modifica.
// =====================================================================================

using System.Buffers.Binary;   // BinaryPrimitives: numeri scritti "big endian" (il byte più importante prima)
using System.Text;

namespace SteamAll.Core.Games;

public enum DiscKind { Unknown, GameCube, Wii, PlayStation, Psp, Cd, Dvd }

public static class RomSniffer
{
    /// <summary>Che disco è questo .iso (dai suoi primi byte).</summary>
    public static DiscKind Iso(string path)
    {
        byte[] head = ReadAt(path, 0, 0x20);
        if (head.Length == 0x20)
        {
            if (BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0x1C)) == 0xC2339F3D) return DiscKind.GameCube;
            if (BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0x18)) == 0x5D1C9EA3) return DiscKind.Wii;
        }
        byte[] pvd = ReadAt(path, 0x8000, 0x30);   // il "descrittore di volume" di ISO 9660
        if (pvd.Length == 0x30 && Encoding.ASCII.GetString(pvd, 1, 5) == "CD001")
        {
            string system = Encoding.ASCII.GetString(pvd, 8, 32).Trim();
            if (system.StartsWith("PSP GAME", StringComparison.Ordinal)) return DiscKind.Psp;
            if (system.StartsWith("PLAYSTATION", StringComparison.Ordinal)) return DiscKind.PlayStation;
        }
        return DiscKind.Unknown;
    }

    /// <summary>Un .chd contiene un CD o un DVD? (Unknown se non si capisce.)</summary>
    public static DiscKind Chd(string path)
    {
        byte[] header = ReadAt(path, 0, 64);
        if (header.Length < 64 || Encoding.ASCII.GetString(header, 0, 8) != "MComprHD") return DiscKind.Unknown;
        uint version = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12));
        // Dove iniziano i metadati: alla posizione 48 nella versione 5, 36 nelle 3 e 4.
        ulong offset = version >= 5 ? BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(48))
                     : version >= 3 ? BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(36)) : 0;
        // Ogni voce dei metadati: 4 byte di "etichetta", 4 di dimensione, 8 con la posizione della voce dopo.
        for (int i = 0; i < 32 && offset > 0; i++)
        {
            byte[] entry = ReadAt(path, (long)offset, 16);
            if (entry.Length < 16) break;
            string tag = Encoding.ASCII.GetString(entry, 0, 4);
            if (tag == "DVD ") return DiscKind.Dvd;
            if (tag is "CHT2" or "CHTR" or "CHCD" or "CHGT" or "CHGD") return DiscKind.Cd;
            offset = BinaryPrimitives.ReadUInt64BigEndian(entry.AsSpan(8));
        }
        return DiscKind.Unknown;
    }

    // Le parole nei nomi delle cartelle che indicano un sistema.
    private static readonly (string System, string[] Words)[] FolderHints =
    [
        ("ps2", ["ps2", "playstation 2", "playstation2", "pcsx2"]),
        ("ps1", ["ps1", "psx", "playstation 1", "playstation1", "playstation", "duckstation"]),
        ("psp", ["psp", "ppsspp"]),
        ("x360", ["xbox 360", "xbox360", "x360", "xenia"]),
        ("gc", ["gc", "gamecube", "ngc"]),
        ("wii", ["wii"]),
    ];

    /// <summary>Il sistema indicato dalle cartelle del percorso ("ps2", "psp"...), o "".</summary>
    public static string FolderHint(string path)
    {
        // Le cartelle dalla più vicina al file: la prima che indica un sistema vince.
        string? dir = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(dir))
        {
            string name = Path.GetFileName(dir).ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');
            foreach ((string system, string[] words) in FolderHints)
                if (words.Contains(name)) return system;
            dir = Path.GetDirectoryName(dir);
        }
        return "";
    }

    private static byte[] ReadAt(string path, long offset, int count)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (file.Length < offset + count) return [];
            file.Position = offset;
            var buffer = new byte[count];
            file.ReadExactly(buffer);   // legge esattamente "count" byte (o dà errore)
            return buffer;
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }
}
