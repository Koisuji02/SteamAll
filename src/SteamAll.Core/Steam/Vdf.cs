// =====================================================================================
//  Vdf.cs
//  Il formato "KeyValues binario" di Valve, quello di shortcuts.vdf (i giochi non Steam).
//
//  È un albero di voci; ogni voce è:   1 byte con il tipo | il nome | il valore
//    0 = mappa (contiene altre voci, finisce con un byte 8)   1 = testo (finisce con un byte 0)
//    2 = intero a 32 bit (4 byte)   3 = decimale a 32 bit (4 byte)   7 = intero a 64 bit (8 byte)
//  Esempio semplificato:  0 "shortcuts" { 0 "0" { 2 "appid" 1234  1 "AppName" "Kirby" ... 8 } 8 } 8
//
//  Ogni voce resta com'era, byte per byte: riscrivendo il file senza modifiche si ottiene lo
//  stesso identico file. Così non rischiamo di rovinare campi che non conosciamo.
// =====================================================================================

using System.Text;

namespace SteamAll.Core.Steam;

// "enum" = un elenco di valori con un nome. ": byte" = ogni valore occupa un byte, come nel file.
public enum VdfType : byte
{
    Map = 0,
    String = 1,
    Int32 = 2,
    Float32 = 3,
    UInt64 = 7,
}

/// <summary>Una voce del file: tipo, nome e valore.</summary>
public sealed class VdfEntry
{
    // "{ get; set; }" = proprietà: si legge e si scrive come un campo. "= []" = valore iniziale:
    // un array vuoto ("[]" è la "collection expression" del C# moderno).
    public VdfType Type { get; set; }
    public byte[] KeyBytes { get; set; } = [];   // il nome, così come sta nel file (UTF-8)
    public byte[] Data { get; set; } = [];       // il valore, così come sta nel file
    public VdfNode? Child { get; set; }          // il contenuto, se la voce è una mappa (altrimenti null)

    // Il nome come testo: get e set "traducono" da e verso i byte.
    // "=>" dopo get/set = il corpo in una riga (come scrivere { return ...; }).
    public string Key
    {
        get => Encoding.UTF8.GetString(KeyBytes);
        set => KeyBytes = Encoding.UTF8.GetBytes(value);   // "value" = il valore assegnato
    }

    // Il valore come testo, anche per i numeri; "" per le mappe.
    // Un'espressione "switch" sceglie un risultato in base al valore; "_" vuol dire "qualunque altro".
    public string Text => Type switch
    {
        VdfType.String => Encoding.UTF8.GetString(Data),
        VdfType.Int32 => BitConverter.ToUInt32(Data).ToString(),
        VdfType.UInt64 => BitConverter.ToUInt64(Data).ToString(),
        _ => "",
    };

    // Metodi "fabbrica": creano una voce già pronta.
    // "new() { ... }" = crea l'oggetto (il tipo lo deduce dal tipo di ritorno) e imposta le proprietà.
    public static VdfEntry FromString(string key, string value) =>
        new() { Type = VdfType.String, Key = key, Data = Encoding.UTF8.GetBytes(value) };

    public static VdfEntry FromUInt32(string key, uint value) =>
        new() { Type = VdfType.Int32, Key = key, Data = BitConverter.GetBytes(value) };   // 4 byte

    public static VdfEntry FromMap(string key, VdfNode child) =>
        new() { Type = VdfType.Map, Key = key, Child = child };
}

/// <summary>Una mappa: un elenco ordinato di voci.</summary>
public sealed class VdfNode
{
    // Solo "get": l'elenco si può modificare (aggiungere, togliere), ma non sostituire con un altro.
    public List<VdfEntry> Entries { get; } = [];

    // La voce con questo nome; Steam non distingue maiuscole e minuscole ("AppName" = "appname").
    // FirstOrDefault (LINQ) = il primo elemento che rispetta la condizione, o null se non ce n'è.
    // "e => ..." è una lambda: un piccolo metodo scritto al volo, qui la condizione da controllare.
    public VdfEntry? Find(string key) =>
        Entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

    // "?." = se Find dà null, tutto vale null; "??" = se è null, usa "".
    public string GetText(string key) => Find(key)?.Text ?? "";

    public VdfNode? GetNode(string key) => Find(key)?.Child;

    // I testi di una lista come "tags": { "0" "Gog"  "1" "Installed" }.
    // Where filtra, Select trasforma: insieme leggono "i testi delle voci di tipo String".
    public List<string> Values() => Entries.Where(e => e.Type == VdfType.String).Select(e => e.Text).ToList();

    /// <summary>Cambia (o aggiunge) un testo.</summary>
    public void SetText(string key, string value)
    {
        VdfEntry? entry = Find(key);
        if (entry is null)
        {
            Entries.Add(VdfEntry.FromString(key, value));
            return;
        }
        entry.Type = VdfType.String;
        entry.Child = null;
        entry.Data = Encoding.UTF8.GetBytes(value);
    }
}

/// <summary>Lettura e scrittura del formato binario.</summary>
public static class BinaryVdf
{
    private const byte EndOfMap = 8;   // "const" = costante fissata quando si compila

    public static VdfNode Read(byte[] data)
    {
        int pos = 0;
        VdfNode root = ReadNode(data, ref pos);
        if (pos != data.Length)
            throw new InvalidDataException("VDF: data after the end of the file");
        return root;
    }

    // "ref int pos": il metodo riceve la variabile stessa, non una copia: quando la fa avanzare,
    // anche chi lo ha chiamato vede la nuova posizione. Così i metodi si passano il "segno" di
    // lettura. ReadNode chiama se stesso per le mappe dentro le mappe ("ricorsione").
    private static VdfNode ReadNode(byte[] data, ref int pos)
    {
        var node = new VdfNode();
        while (true)
        {
            byte type = TakeByte(data, ref pos);
            if (type == EndOfMap) return node;

            var entry = new VdfEntry { Type = (VdfType)type, KeyBytes = ReadUntilZero(data, ref pos) };
            switch (entry.Type)   // lo "switch" classico: esegue il ramo del valore e poi "break"
            {
                case VdfType.Map: entry.Child = ReadNode(data, ref pos); break;
                case VdfType.String: entry.Data = ReadUntilZero(data, ref pos); break;
                case VdfType.Int32:
                case VdfType.Float32: entry.Data = Take(data, ref pos, 4); break;   // due "case" con lo stesso codice
                case VdfType.UInt64: entry.Data = Take(data, ref pos, 8); break;
                default: throw new InvalidDataException($"VDF: unknown type {type}");
            }
            node.Entries.Add(entry);
        }
    }

    private static byte TakeByte(byte[] data, ref int pos)
    {
        if (pos >= data.Length) throw new InvalidDataException("VDF: truncated file");
        return data[pos++];   // pos++ = usa pos, poi aumentalo di 1
    }

    private static byte[] ReadUntilZero(byte[] data, ref int pos)
    {
        // Array.IndexOf cerca il primo byte 0 a partire da pos; -1 = non c'è.
        int end = Array.IndexOf(data, (byte)0, pos);
        if (end < 0) throw new InvalidDataException("VDF: truncated file");
        byte[] result = data[pos..end];   // "pos..end" = un "intervallo": i byte da pos a end (escluso)
        pos = end + 1;                    // salta lo 0
        return result;
    }

    private static byte[] Take(byte[] data, ref int pos, int count)
    {
        if (pos + count > data.Length) throw new InvalidDataException("VDF: truncated file");
        byte[] result = data[pos..(pos + count)];
        pos += count;
        return result;
    }

    public static byte[] Write(VdfNode root)
    {
        using var output = new MemoryStream();
        WriteNode(output, root);
        return output.ToArray();
    }

    private static void WriteNode(MemoryStream output, VdfNode node)
    {
        foreach (VdfEntry entry in node.Entries)
        {
            output.WriteByte((byte)entry.Type);   // (byte) = "cast": da VdfType al suo numero
            output.Write(entry.KeyBytes);
            output.WriteByte(0);
            if (entry.Type == VdfType.Map)
            {
                // "!" dice al compilatore "so che qui Child non è null" (una mappa ha sempre il suo contenuto).
                WriteNode(output, entry.Child!);
                continue;   // passa alla voce successiva
            }
            output.Write(entry.Data);
            if (entry.Type == VdfType.String) output.WriteByte(0);
        }
        output.WriteByte(EndOfMap);
    }
}
