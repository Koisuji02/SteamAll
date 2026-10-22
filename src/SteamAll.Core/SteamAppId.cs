// =========================================================================
//  SteamAppId.cs
//  L'"app id" che Steam dà a un gioco non Steam: un numero calcolato dall'exe e dal nome.
//
//  Le immagini della libreria si chiamano con questo numero (grid\<appid>p.png è la copertina), e le raccolte e il tempo di gioco sono legati a lui. Se lo calcoliamo come Steam (e come BoilR), riprendiamo le voci esistenti senza perdere niente.
// =========================================================================

using System.Text; // Encoding: testo -> byte
// "Namespace a livello di file": vale per tutto il file
namespace SteamAll.Core;

// "static class": contiene solo metodi statici, non si crea con "new".
public static class SteamAppId {

    // I commenti con tre barre (///) qui sotto sono "commenti di documentazione": VS Code li  mostra quando passi con il mouse sul nome del metodo, ovunque venga usato.

    /// <summary>
    /// L'app id di una scorciatoia: il CRC32 di "exe tra virgolette" seguito dal nome, con il bit più alto acceso.
    /// </summary>
    /// <param name="exePath">L'exe senza virgolette, per esempio C:\Games\Kirby\Kirby.exe</param>
    /// <param name="name">Il nome in Steam</param>
    public static uint ForShortcut(string exePath, string name) {

        // $"..." è una stringa "interpolata": quello che sta tra { } viene sostituito dal valore
        // \" è una virgoletta dentro la stringa. Risultato: "C:\...\Kirby.exe"Kirby
        byte[] data = Encoding.UTF8.GetBytes($"\"{exePath}\"{name}");

        // 0x8000_0000u: numero esadecimale ("0x"), il "_" serve solo a leggerlo meglio, la "u" lo rende "uint" (intero senza segno). L'operatore | accende il bit 31
        return Crc32(data) | 0x8000_0000u;
    }

    // ---------------------------------------------------------------------
    //  CRC32: un "riassunto" di 32 bit di una sequenza di byte (lo stesso dei file ZIP).
    //  È l'algoritmo standard: una tabella di 256 valori calcolata una volta, poi un ciclo.
    // ---------------------------------------------------------------------

    // "static readonly": creata una volta sola, al primo uso della classe, e mai più cambiata.
    private static readonly uint[] Table = CreateTable();

    private static uint[] CreateTable() {
        var table = new uint[256];   // "var": il tipo (uint[]) lo deduce il compilatore da destra
        for (uint i = 0; i < 256; i++) {
            uint c = i;
            for (int k = 0; k < 8; k++) {
                // (c & 1) prende il bit più basso; ^ è lo XOR; >> sposta i bit a destra di uno.
                c = (c & 1) != 0 ? 0xEDB8_8320u ^ (c >> 1) : c >> 1;
            }
            table[i] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] data) {
        uint crc = 0xFFFF_FFFFu;
        foreach (byte b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return crc ^ 0xFFFF_FFFFu;
    }
}