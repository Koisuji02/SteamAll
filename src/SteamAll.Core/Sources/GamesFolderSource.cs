// =====================================================================================
//  GamesFolderSource.cs
//  Una cartella dei giochi (C:\Games o un'altra scelta da te): una sottocartella per gioco.
//
//      <Cartella>\<Nome del gioco>\<exe dell'emulatore con il nome originale>   -> gioco emulato
//      <Cartella>\<Nome del gioco>\<Nome del gioco>.exe (non creato da noi)     -> gioco PC
//
//  Il nome della sottocartella è il nome in Steam (e quello che SteamGridDB cerca per le
//  immagini: meglio il titolo inglese ufficiale). Per un gioco emulato Steam avvia il launcher:
//  su Windows "<Nome del gioco>.exe", che SteamAll crea accanto all'emulatore; su Linux e macOS
//  SteamAll stesso, con "--launch".
//  Su Linux e macOS un gioco PC è un programma con il nome della cartella: "<Nome>", "<Nome>.sh",
//  "<Nome>.AppImage" o "<Nome>.x86_64".
// =====================================================================================

using System.Diagnostics;   // FileVersionInfo: le informazioni di versione dentro un exe
using SteamAll.Core.Games;
using SteamAll.Core.Platform;

namespace SteamAll.Core.Sources;

public static class GamesFolderSource
{
    /// <summary>Il "nome del prodotto" scritto nei nostri launcher: così li riconosciamo.</summary>
    public const string LauncherProduct = "SteamAll Game Launcher";
    private const string OldLauncherProduct = "BoilR Game Launcher";   // quelli creati da Setup_BoilR

    /// <summary>
    /// I giochi di una cartella, in ordine alfabetico (vuoto se la cartella non c'è).
    /// "selfExe" (solo Linux e macOS) = SteamAll stesso, che fa da launcher dei giochi emulati.
    /// </summary>
    public static List<Game> Scan(string folder, OsKind os = OsKind.Windows, string selfExe = "")
    {
        if (!Directory.Exists(folder)) return [];

        // OrderBy (LINQ) mette in ordine; StringComparer.CurrentCultureIgnoreCase = alfabetico come
        // in Esplora file, senza badare a maiuscole e minuscole.
        var dirs = Directory.GetDirectories(folder).OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase);
        var games = new List<Game>();
        foreach (string dir in dirs)
        {
            // Un problema in un gioco (permessi, file strani) non deve fermare gli altri:
            // lo trasformiamo in un gioco "con problema", che l'interfaccia mostrerà come saltato.
            try
            {
                games.Add(os == OsKind.Windows ? ScanGameFolder(folder, dir) : ScanUnixGameFolder(folder, dir, selfExe));
            }
            catch (Exception ex)
            {
                games.Add(NewGame(folder, dir) with { Problem = ex.Message });
            }
        }
        return games;
    }

    // Il gioco "di base" di una sottocartella: nome, chiave, exe e cartella di avvio.
    private static Game NewGame(string folder, string dir)
    {
        string name = Path.GetFileName(dir);
        return new Game
        {
            // La chiave contiene il percorso intero (in minuscolo): così due cartelle dei giochi
            // diverse possono avere un gioco con lo stesso nome senza confondersi.
            Key = "folder:" + dir.ToLowerInvariant(),
            Name = name,
            Source = folder,
            Origin = GameOrigin.Folder,
            InstallFolder = dir,
            ExePath = Path.Combine(dir, name + ".exe"),
            StartDir = dir,
        };
    }

    // Gli emulatori che si usano nelle cartelle "emulatore portable + gioco".
    private static IEnumerable<Emulator> FolderEmulators => Emulator.All.Where(e => e.FolderMode);

    private static Game ScanGameFolder(string folder, string dir)
    {
        Game game = NewGame(folder, dir);

        // Quali emulatori ci sono? Di solito uno; nessuno per un gioco PC.
        List<Emulator> found = FolderEmulators.Where(e => File.Exists(Path.Combine(dir, e.Exe))).ToList();

        // C'è già un exe con il nome della cartella? È un nostro launcher o il gioco PC?
        bool exeExists = File.Exists(game.ExePath);
        bool ours = exeExists && IsOurLauncher(game.ExePath);

        if (found.Count > 1)
        {
            // string.Join unisce i testi di un elenco con un separatore.
            return game with { Problem = $"More than one emulator in the folder ({string.Join(", ", found.Select(e => e.Exe))}): keep only one" };
        }

        if (found.Count == 1)
        {
            Emulator emulator = found[0];
            game = game with { Kind = emulator.Name, Emulator = emulator, EmulatorExe = Path.Combine(dir, emulator.Exe), Origin = GameOrigin.Folder };
            if (exeExists && !ours)
                return game with { Problem = $"{game.Name}.exe already exists and was not created by SteamAll (is it the renamed emulator?)" };
            return WithGameFile(game, dir, exeExists ? "" : "Launcher will be created");
        }

        // Nessun emulatore. Se ci sono TUTTI i file tipici di un emulatore ma non il suo exe,
        // quasi sicuramente l'exe è stato rinominato. "All" = tutti gli elementi rispettano la condizione.
        Emulator? renamed = FolderEmulators.FirstOrDefault(e =>
            e.Hints.All(h => File.Exists(Path.Combine(dir, h)) || Directory.Exists(Path.Combine(dir, h))));
        if (renamed is not null)
            return game with { Problem = $"Looks like {renamed.Name} but {renamed.Exe} is missing: give the emulator exe its original name" };

        if (exeExists && !ours)
            return game with { Kind = "PC game" };

        // Né emulatore né gioco PC: diciamo quali exe ci sono, per aiutare a capire.
        string[] exes = Directory.GetFiles(dir, "*.exe").Select(Path.GetFileName).OfType<string>().ToArray();
        string list = exes.Length > 0 ? string.Join(", ", exes) : "none";
        return game with { Problem = $"No supported emulator found (exe files: {list})" };
    }

    // Linux e macOS: l'emulatore si riconosce dai nomi del suo programma su questi sistemi.
    private static Game ScanUnixGameFolder(string folder, string dir, string selfExe)
    {
        Game game = NewGame(folder, dir);
        var found = FolderEmulators
            .Select(e => (Emulator: e, Exe: e.ExeNames.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists)))
            .Where(t => t.Exe is not null)
            .ToList();
        if (found.Count > 1)
            return game with { Problem = $"More than one emulator in the folder ({string.Join(", ", found.Select(t => Path.GetFileName(t.Exe)))}): keep only one" };
        if (found.Count == 1)
        {
            game = game with { Kind = found[0].Emulator.Name, Emulator = found[0].Emulator, EmulatorExe = found[0].Exe!, ExePath = selfExe };
            return WithGameFile(game, dir, "");
        }

        string name = Path.GetFileName(dir);
        string? pc = new[] { name, name + ".sh", name + ".AppImage", name + ".x86_64" }.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists);
        if (pc is not null) return game with { Kind = "PC game", ExePath = pc };
        return game with { Problem = $"No supported emulator and no program called \"{name}\" in the folder" };
    }

    // Il file del gioco (rom, iso...): se non si trova, GameFileFinder lancia un errore con il
    // motivo, che Scan trasforma nel "problema" del gioco.
    private static Game WithGameFile(Game game, string dir, string note)
    {
        GameFile file = GameFileFinder.Find(dir, game.Emulator!);
        if (file.Note.Length > 0) note = note.Length > 0 ? $"{note}; {file.Note}" : file.Note;
        return game with { RomPath = file.RelativePath, Note = note };
    }

    /// <summary>
    /// Un exe creato da noi (o da Setup_BoilR) ha il nostro nome di prodotto nelle informazioni di
    /// versione: le stesse che vedi in Esplora file > Proprietà > Dettagli.
    /// </summary>
    public static bool IsOurLauncher(string exe) =>
        FileVersionInfo.GetVersionInfo(exe).ProductName is LauncherProduct or OldLauncherProduct;
}
