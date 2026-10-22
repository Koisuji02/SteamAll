// =====================================================================================
//  SteamLibraryTests.cs
//  Prove degli utenti di Steam e della lettura dei giochi non Steam, su una cartella di Steam
//  FINTA creata in una cartella temporanea: niente dipende dal PC su cui girano.
// =====================================================================================

using SteamAll.Core.Steam;

namespace SteamAll.Tests;

// ": IDisposable" = la classe ha un metodo Dispose, che xUnit chiama alla fine di ogni prova:
// lo usiamo per cancellare la cartella finta (è nostra e temporanea, non serve il Cestino).
public class SteamLibraryTests : IDisposable
{
    private readonly string steamFolder;   // "readonly": si assegna solo nel costruttore

    // xUnit crea un oggetto nuovo per OGNI prova: il costruttore prepara una cartella pulita.
    public SteamLibraryTests()
    {
        steamFolder = Path.Combine(Path.GetTempPath(), "steamall-test-" + Guid.NewGuid().ToString("N"));

        // Utente 111: più vecchio, senza giochi non Steam.
        string old = Path.Combine(steamFolder, "userdata", "111", "config");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "localconfig.vdf"), "\"UserLocalConfigStore\" { \"friends\" { \"PersonaName\" \"oldname\" } }");
        File.SetLastWriteTimeUtc(Path.Combine(old, "localconfig.vdf"), new DateTime(2020, 1, 1));

        // Utente 222: il più recente, con tre giochi e una copertina.
        string config = Path.Combine(steamFolder, "userdata", "222", "config");
        Directory.CreateDirectory(Path.Combine(config, "grid"));
        File.WriteAllText(Path.Combine(config, "localconfig.vdf"), "\"UserLocalConfigStore\" { \"friends\" { \"PersonaName\" \"player\" } }");

        VdfNode root = ShortcutLibrary.EmptyLibrary();
        VdfNode list = root.GetNode("shortcuts")!;
        list.Entries.Add(VdfEntry.FromMap("0", Shortcut.CreateNode(
            4113688513, "Kirby", "\"C:\\Games\\Kirby\\Kirby.exe\"", "\"C:\\Games\\Kirby\"", "", "", "boilr", true)));
        list.Entries.Add(VdfEntry.FromMap("1", Shortcut.CreateNode(
            2878101719, "GreedFall", "\"C:\\WINDOWS\\System32\\rundll32.exe\"", "\"C:\\WINDOWS\\System32\"", "",
            "url.dll,FileProtocolHandler goggalaxy://openGameView/gog_1632", Markers.SetupBoilRPlaceholder, false)));
        list.Entries.Add(VdfEntry.FromMap("2", Shortcut.CreateNode(
            3000000000, "My own game", "\"D:\\mine.exe\"", "\"D:\\\"", "", "", "", true)));
        File.WriteAllBytes(Path.Combine(config, "shortcuts.vdf"), BinaryVdf.Write(root));

        File.WriteAllBytes(Path.Combine(config, "grid", "4113688513p.png"), [1, 2, 3]);   // una "copertina" finta
        File.WriteAllBytes(Path.Combine(config, "grid", "2878101719p.png"), []);          // vuota: non conta
    }

    public void Dispose() => Directory.Delete(steamFolder, recursive: true);   // "recursive:" = argomento con nome

    [Fact]
    public void ChooseUser_WithoutPreference_TakesTheLastUsed()
    {
        SteamUser? user = new SteamInstall(steamFolder).ChooseUser(null);

        Assert.NotNull(user);
        Assert.Equal("222", user.Id);
        Assert.Equal("player (222)", user.ToString());
    }

    [Fact]
    public void ChooseUser_WithPreference_TakesThatOne()
    {
        SteamUser? user = new SteamInstall(steamFolder).ChooseUser("111");

        Assert.Equal("111", user?.Id);
    }

    [Fact]
    public void Load_ReadsTheGamesAndWhoAddedThem()
    {
        SteamUser user = new SteamInstall(steamFolder).ChooseUser(null)!;

        ShortcutLibrary library = ShortcutLibrary.Load(user);

        Assert.Equal(3, library.Shortcuts.Count);
        Shortcut kirby = library.Shortcuts[0], greedFall = library.Shortcuts[1], mine = library.Shortcuts[2];
        Assert.True(kirby.IsBoilR);
        Assert.True(kirby.IsManaged);
        Assert.True(greedFall.IsPlaceholder);
        Assert.Equal("goggalaxy://openGameView/gog_1632", greedFall.StoreUri);
        Assert.False(mine.IsManaged);   // aggiunto a mano: non lo toccheremo mai
    }

    [Fact]
    public void FindImage_IgnoresMissingAndEmptyFiles()
    {
        ShortcutLibrary library = ShortcutLibrary.Load(new SteamInstall(steamFolder).ChooseUser(null)!);

        Assert.NotNull(library.FindImage(4113688513, "p"));
        Assert.Null(library.FindImage(2878101719, "p"));   // il file c'è ma è vuoto
        Assert.Null(library.FindImage(3000000000, "p"));   // il file non c'è
    }
}
