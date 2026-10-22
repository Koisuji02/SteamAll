// =====================================================================================
//  TempFolder.cs
//  Una cartella temporanea per le prove: si crea vuota e si cancella da sola alla fine.
//  Si usa così:   using var temp = new TempFolder();   ...   (alla fine del blocco sparisce)
// =====================================================================================

namespace SteamAll.Tests;

// ": IDisposable" = ha un metodo Dispose, che "using" chiama da solo alla fine.
// La cartella è nostra e temporanea: si cancella direttamente, senza Cestino.
public sealed class TempFolder : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "steamall-test-" + Guid.NewGuid().ToString("N"));
    // Qui scriviamo System.IO.Path per intero: "Path" da solo sarebbe la proprietà qui sopra.

    public TempFolder() => Directory.CreateDirectory(Path);

    /// <summary>Crea un file (e le sue cartelle) dentro la cartella temporanea; restituisce il percorso completo.</summary>
    public string File(string relativePath, string content = "")
    {
        string full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    /// <summary>Crea una cartella (vuota) dentro la cartella temporanea.</summary>
    public string Folder(string relativePath) => Directory.CreateDirectory(System.IO.Path.Combine(Path, relativePath)).FullName;

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}
