// =====================================================================================
//  Protobuf.cs
//  Un lettore minimo del formato "Protocol Buffers" (protobuf), il formato binario di Google che
//  Ubisoft Connect usa per la sua cache. Ci basta leggere i campi uno dopo l'altro.
//
//  Un messaggio protobuf è una fila di campi. Ogni campo inizia con una "chiave" = numero del
//  campo * 8 + "tipo di filo" (wire type), scritta come varint. Poi il valore, a seconda del tipo:
//    0  varint           un numero intero
//    1  64 bit           8 byte fissi
//    2  con lunghezza    una varint con la lunghezza, poi i byte (testi, messaggi annidati)
//    5  32 bit           4 byte fissi
//  Una "varint" scrive un numero 7 bit alla volta: ogni byte usa i 7 bit bassi per il numero e
//  il bit alto (0x80) per dire "ne segue un altro". Così i numeri piccoli occupano un byte solo.
// =====================================================================================

namespace SteamAll.Core.Sources;

internal static class Protobuf
{
    /// <summary>Un campo letto: il suo numero, il tipo e il valore (numero, oppure i byte).</summary>
    // "readonly record struct": un piccolo valore immutabile, senza allocazioni come una classe.
    public readonly record struct Field(int Number, int WireType, ulong Number64, ReadOnlyMemory<byte> Bytes)
    {
        public string Text => System.Text.Encoding.UTF8.GetString(Bytes.Span);
    }

    /// <summary>I campi di un messaggio, in ordine. Errore (InvalidDataException) se i byte non sono protobuf.</summary>
    public static List<Field> Read(ReadOnlyMemory<byte> data)
    {
        var fields = new List<Field>();
        ReadOnlySpan<byte> b = data.Span;   // Span = una "finestra" sui byte, senza copiarli
        int i = 0;
        while (i < b.Length)
        {
            ulong key = Varint(b, ref i);   // "ref i": il metodo sposta in avanti la posizione
            int number = (int)(key >> 3), wire = (int)(key & 7);
            if (number == 0) throw new InvalidDataException("protobuf: field number 0");
            switch (wire)
            {
                case 0:
                    fields.Add(new Field(number, wire, Varint(b, ref i), ReadOnlyMemory<byte>.Empty));
                    break;
                case 1:
                case 5:
                    int size = wire == 1 ? 8 : 4;
                    if (i + size > b.Length) throw new InvalidDataException("protobuf: truncated");
                    fields.Add(new Field(number, wire, 0, data.Slice(i, size)));
                    i += size;
                    break;
                case 2:
                    ulong length = Varint(b, ref i);
                    if (length > (ulong)(b.Length - i)) throw new InvalidDataException("protobuf: truncated");
                    fields.Add(new Field(number, wire, 0, data.Slice(i, (int)length)));
                    i += (int)length;
                    break;
                default:
                    throw new InvalidDataException($"protobuf: unsupported wire type {wire}");
            }
        }
        return fields;
    }

    private static ulong Varint(ReadOnlySpan<byte> b, ref int i)
    {
        ulong value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if (i >= b.Length) throw new InvalidDataException("protobuf: truncated varint");
            byte x = b[i++];
            value |= (ulong)(x & 0x7F) << shift;   // i 7 bit bassi, spostati al loro posto
            if ((x & 0x80) == 0) return value;     // bit alto spento: era l'ultimo byte
        }
        throw new InvalidDataException("protobuf: varint too long");
    }
}
