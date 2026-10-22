// =====================================================================================
//  GamesFolderSourceTests.cs
//  Prove della scansione di una cartella dei giochi, su una cartella FINTA: ogni sottocartella
//  è un caso diverso (gioco emulato, gioco PC, problemi).
// =====================================================================================

using SteamAll.Core.Games;
using SteamAll.Core.Sources;

namespace SteamAll.Tests;

public class GamesFolderSourceTests
{
    [Fact]
    public void Scan_RecognisesEachKindOfFolder()
    {
        using var temp = new TempFolder();
        temp.File(@"Kirby\Ryujinx.exe");                          // emulato, launcher ancora da creare
        temp.File(@"Kirby\games\Kirby [0100227010460000].nsp");   // il file del gioco
        temp.File(@"The Bells Arietta\The Bells Arietta.exe");    // gioco PC (exe con il nome della cartella)
        temp.File(@"Two Emus\Cemu.exe");                          // due emulatori: problema
        temp.File(@"Two Emus\Dolphin.exe");
        temp.File(@"Renamed\DolphinTool.exe");                    // file di Dolphin senza Dolphin.exe: rinominato
        temp.Folder(@"Renamed\Sys");
        temp.File(@"Renamed\MyDolphin.exe");
        temp.Folder("Empty");                                     // niente di riconoscibile

        List<Game> games = GamesFolderSource.Scan(temp.Path);

        // ToDictionary: dall'elenco a un dizionario nome -> gioco, per prenderli per nome.
        var byName = games.ToDictionary(g => g.Name);
        Assert.Equal(5, games.Count);

        Game kirby = byName["Kirby"];
        Assert.Equal("Ryujinx (Switch)", kirby.Kind);
        Assert.Null(kirby.Problem);
        Assert.Equal("Launcher will be created", kirby.Note);
        Assert.Equal(@"games\Kirby [0100227010460000].nsp", kirby.RomPath);
        Assert.Equal(Path.Combine(temp.Path, "Kirby", "Kirby.exe"), kirby.ExePath);
        Assert.Equal(temp.Path, kirby.Source);

        Assert.Equal("PC game", byName["The Bells Arietta"].Kind);
        Assert.Null(byName["The Bells Arietta"].Problem);

        Assert.Contains("More than one emulator", byName["Two Emus"].Problem);
        Assert.Contains("Dolphin.exe is missing", byName["Renamed"].Problem);
        Assert.Contains("No supported emulator", byName["Empty"].Problem);
    }

    [Fact]
    public void Scan_EmulatedGameWithForeignExe_IsSkipped()
    {
        // C'è l'emulatore, ma anche un exe con il nome della cartella che non è un nostro launcher:
        // potrebbe essere l'emulatore rinominato, quindi non lo tocchiamo.
        using var temp = new TempFolder();
        temp.File(@"Zelda\Cemu.exe");
        temp.File(@"Zelda\Zelda.exe");

        Game zelda = Assert.Single(GamesFolderSource.Scan(temp.Path));   // Single: deve essercene esattamente uno

        Assert.Contains("was not created by SteamAll", zelda.Problem);
    }

    [Fact]
    public void Scan_MissingFolder_GivesNoGames()
    {
        Assert.Empty(GamesFolderSource.Scan(@"Z:\this\folder\does\not\exist"));
    }
}
