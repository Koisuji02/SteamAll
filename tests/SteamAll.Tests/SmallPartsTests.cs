// =====================================================================================
//  SmallPartsTests.cs
//  Prove dei pezzi piccoli delle Fasi 2 e 3: file del gioco, comandi di avvio GOG, ricerca su
//  SteamGridDB, immagini, pulizia.
// =====================================================================================

using SteamAll.Core.Apply;
using SteamAll.Core.Artwork;
using SteamAll.Core.Games;
using SteamAll.Core.Steam;
using SteamAll.Core.Stores;

namespace SteamAll.Tests;

public class SmallPartsTests : IDisposable
{
    private readonly TempFolder temp = new();
    public void Dispose() => temp.Dispose();

    [Fact]
    public void GameFile_Switch_SkipsUpdatesAndDlc()
    {
        string dir = temp.Folder("Kirby");
        temp.File(@"Kirby\games\Kirby [01004D300C5AE000].nsp", "base");
        temp.File(@"Kirby\games\Kirby [01004D300C5AE800][v65536].nsp", "update");
        temp.File(@"Kirby\games\Kirby DLC [01004D300C5AF001].nsp", "dlc");

        GameFile file = GameFileFinder.Find(dir, Emulator.All.Single(e => e.ControllerKind == "Ryujinx"));

        Assert.Equal(@"games\Kirby [01004D300C5AE000].nsp", file.RelativePath);
    }

    [Fact]
    public void GameFile_Dolphin_TwoDiscs_StartsFromDiscOne()
    {
        string dir = temp.Folder("Tales");
        temp.File(@"Tales\games\Tales (Disc 1).rvz");
        temp.File(@"Tales\games\Tales (Disc 2).rvz");

        GameFile file = GameFileFinder.Find(dir, Emulator.All.Single(e => e.ControllerKind == "Dolphin"));

        Assert.Equal(@"games\Tales (Disc 1).rvz", file.RelativePath);
        Assert.Contains("disc 1", file.Note);
    }

    [Fact]
    public void GameFile_Missing_ExplainsWhy()
    {
        string dir = temp.Folder("Empty");

        var error = Assert.Throws<InvalidDataException>(() => GameFileFinder.Find(dir, Emulator.All.Single(e => e.ControllerKind == "Cemu")));

        Assert.Contains("no Wii U game", error.Message);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]   // GogGalaxyStore è solo per Windows
    public void Gog_PrimaryPlayTask_IsTheGameExe()
    {
        string dir = temp.Folder("Witcher");
        temp.File(@"Witcher\goggame-14.info", """
            { "playTasks": [
                { "type": "FileTask", "path": "Manual.pdf", "name": "Manual" },
                { "isPrimary": true, "type": "FileTask", "path": "bin\\x64\\witcher3.exe", "arguments": "-skip" } ] }
            """);
        temp.File(@"Witcher\goggame-14.ico");

        GogGalaxyStore.GogPlayTask task = GogGalaxyStore.ReadPlayTask(dir, "14")!;

        Assert.Equal(Path.Combine(dir, "bin", "x64", "witcher3.exe"), task.Exe);
        Assert.Equal("-skip", task.Arguments);
        Assert.EndsWith("goggame-14.ico", task.Icon);
    }

    [Fact]
    public void SteamGridDb_SearchAttempts_TryShorterTitles()
    {
        List<string> attempts = SteamGridDbClient.SearchAttempts("Middle-earth™: Shadow of Mordor™ Game of the Year Edition").ToList();

        Assert.Equal("Middle-earth: Shadow of Mordor Game of the Year Edition", attempts[0]);
        Assert.Contains("Middle-earth: Shadow of Mordor", attempts);
        Assert.Contains("Middle-earth", attempts);
    }

    [Fact]
    public void ImageHeader_GivesSizeAndFormat()
    {
        byte[] png = FakeHttp.Png(3840, 1240);

        Assert.Equal("png", ArtworkFiles.Extension(png));
        Assert.Equal((3840, 1240), ArtworkFiles.Size(png));
        Assert.Null(ArtworkFiles.Extension("RIFF....WEBP"u8.ToArray()));   // WebP: Steam non lo usa
    }

    [Fact]
    public void Cleanup_TakesOnlyOrphanNonSteamImages()
    {
        string config = temp.Folder(@"userdata\1\config");
        VdfNode root = ShortcutLibrary.EmptyLibrary();
        root.GetNode("shortcuts")!.Entries.Add(VdfEntry.FromMap("0", Shortcut.CreateNode(3_000_000_001, "Alive", "\"x\"", "", "", "", "", true)));
        File.WriteAllBytes(Path.Combine(config, "shortcuts.vdf"), BinaryVdf.Write(root));
        temp.File(@"userdata\1\config\grid\3000000001p.png", "alive");     // di una voce che c'è
        temp.File(@"userdata\1\config\grid\3000000002_hero.jpg", "gone");  // orfana
        temp.File(@"userdata\1\config\grid\620p.png", "steam game");       // di un gioco Steam vero: mai
        temp.File(@"userdata\1\config\grid\3000000001_logo.png");          // vuota: download fallito
        ShortcutLibrary library = ShortcutLibrary.Load(new SteamUser("1", "", config, DateTime.UtcNow));

        List<string> names = GridCleanup.Candidates(library).Select(f => f.Name).Order().ToList();

        Assert.Equal(["3000000001_logo.png", "3000000002_hero.jpg"], names);
    }
}
