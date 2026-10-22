// =====================================================================================
//  Files.cs
//  Piccoli aiuti per i file, usati in più punti del nucleo.
// =====================================================================================

namespace SteamAll.Core;

public static class Files
{
    /// <summary>
    /// Legge tutto un file anche se un altro programma (Steam, Epic, GOG GALAXY) lo tiene aperto.
    /// </summary>
    public static byte[] ReadShared(string path)
    {
        // File.ReadAllBytes chiederebbe di essere l'unico a usare il file e fallirebbe se Steam lo
        // ha aperto. Con FileShare diciamo che agli altri va bene continuare a scriverlo (o
        // cancellarlo) mentre leggiamo. L'operatore | unisce le due opzioni in un solo valore.
        //
        // "using var" chiude il file alla fine del metodo, anche se nel mezzo c'è un errore.
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        file.CopyTo(memory);
        return memory.ToArray();
    }
}
