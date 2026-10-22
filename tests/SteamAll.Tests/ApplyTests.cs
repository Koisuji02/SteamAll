// =====================================================================================
//  ApplyTests.cs
//  La prova più importante: "Apply to Steam" su una copia FINTA di tutto (cartella di Steam,
//  cartella dei giochi, store, SteamGridDB). Controlliamo che la libreria scritta sia giusta,
//  che Steam venga chiuso e riaperto, che i launcher e le immagini arrivino, e la pulizia.
// =====================================================================================

using SteamAll.Core;
using SteamAll.Core.Apply;
using SteamAll.Core.Games;
using SteamAll.Core.Launchers;
using SteamAll.Core.Plan;
using SteamAll.Core.Settings;
using SteamAll.Core.Sources;
using SteamAll.Core.Steam;
using SteamAll.Launcher;

namespace SteamAll.Tests;

public class ApplyTests : IDisposable
{
    private readonly TempFolder temp = new();
    public void Dispose() => temp.Dispose();

    // SteamGridDB finto: trova ogni gioco (id 1) e dà un PNG per ogni tipo di immagine.
    private static FakeHttp SteamGridDb() => new(request =>
    {
        string url = request.RequestUri!.ToString();
        if (url.Contains("/search/autocomplete/")) return FakeHttp.Json("""{"success":true,"data":[{"id":1,"name":"Found"}]}""");
        if (url.Contains("/api/v2/")) return FakeHttp.Json("""{"success":true,"data":[{"url":"https://img.test/a.png","width":3840,"height":1240,"language":"en"}]}""");
        return FakeHttp.Bytes(FakeHttp.Png(3840, 1240));
    });

    [Fact]
    public async Task Apply_WritesTheLibrary_LaunchersAndImages()
    {
        // --- Steam finto: Kirby (BoilR), il segnaposto di GreedFall, una voce morta, un'immagine orfana.
        string steam = temp.Folder("Steam");
        string config = temp.Folder(@"Steam\userdata\1\config");
        string games = temp.Folder("Games");
        string kirbyExe = Path.Combine(games, "Kirby", "Kirby.exe");
        VdfNode root = ShortcutLibrary.EmptyLibrary();
        VdfNode list = root.GetNode("shortcuts")!;
        list.Entries.Add(VdfEntry.FromMap("0", Shortcut.CreateNode(SteamAppId.ForShortcut(kirbyExe, "Kirby"), "Kirby",
            $"\"{kirbyExe}\"", "", "", "", "boilr", true)));
        list.Entries.Add(VdfEntry.FromMap("1", Shortcut.CreateNode(2_878_101_719, "GreedFall", "\"C:\\WINDOWS\\System32\\rundll32.exe\"",
            "", "", "url.dll,FileProtocolHandler goggalaxy://openGameView/gog_1", Markers.SetupBoilRPlaceholder, false)));
        list.Entries.Add(VdfEntry.FromMap("2", Shortcut.CreateNode(3_333_333_333, "Removed", "\"Z:\\gone\\gone.exe\"", "", "", "", "boilr", true)));
        File.WriteAllBytes(Path.Combine(config, "shortcuts.vdf"), BinaryVdf.Write(root));
        temp.File(@"Steam\userdata\1\config\grid\4000000000p.png", "orphan");   // nessuna voce con questo app id

        // --- Cartella dei giochi: Kirby (PC game: exe con il nome della cartella) e Zelda (Cemu, portable).
        temp.File(@"Games\Kirby\Kirby.exe", "pc game");
        temp.File(@"Games\Zelda\Cemu.exe", "emulator");
        temp.Folder(@"Games\Zelda\portable");
        temp.File(@"Games\Zelda\games\Zelda.wua", "game");

        // --- Store: GreedFall ora installato (GOG), Rage 2 non installato (Epic).
        string greedFolder = temp.Folder(@"GOG\GreedFall");
        temp.File(@"GOG\GreedFall\goggame-1.info", """{"playTasks":[{"isPrimary":true,"type":"FileTask","path":"GreedFall.exe"}]}""");
        temp.File(@"GOG\GreedFall\GreedFall.exe", "game");
        var gog = new SourceResult("GOG GALAXY", SourceKind.Store, "gog",
        [
            new Game { Key = "gog_1", Name = "GreedFall", Source = "GOG GALAXY", Kind = "GOG", IsInstalled = true, Origin = GameOrigin.Store,
                       StoreId = "gog", ProviderId = "gog", InstallFolder = greedFolder, StoreUri = "goggalaxy://openGameView/gog_1" },
        ]);
        var epic = new SourceResult(EpicSource.SourceName, SourceKind.Store, "epic",
        [
            new Game { Key = "epic:r2", Name = "RAGE 2", Source = EpicSource.SourceName, Kind = "Epic", IsInstalled = false, Origin = GameOrigin.Store,
                       StoreId = "epic", ProviderId = "epic", EpicAppName = "Rage2", StoreUri = "com.epicgames.launcher://apps/ns%3Ar2%3ARage2?action=launch" },
        ]);

        var platform = new FakePlatform(steam);
        platform.ProtocolExes["com.epicgames.launcher"] = @"C:\Epic\EpicGamesLauncher.exe";
        var settings = new AppSettings { GameFolders = [games] };
        settings.SetSteamGridDbKey(platform, "fake-key");
        ShortcutLibrary library = ShortcutLibrary.Load(new SteamUser("1", "", config, DateTime.UtcNow));
        ImportPlan plan = ImportPlan.Create(library, [new SourceResult(games, SourceKind.Folder, Markers.FolderStore, GamesFolderSource.Scan(games)), gog, epic]);
        var http = SteamGridDb();
        var applier = new SteamApplier(platform, new HttpClient(http));

        ApplySummary summary = applier.Summarize(plan, settings);
        ApplyReport report = await applier.ApplyAsync(plan, settings, Path.Combine(temp.Path, "artwork.json"));

        // --- Steam chiuso e riaperto, copia di sicurezza fatta.
        Assert.True(summary.SteamWillRestart);
        Assert.Equal(1, platform.Shutdowns);
        Assert.Equal(1, platform.Starts);
        Assert.True(File.Exists(Path.Combine(config, "shortcuts.vdf.steamall-backup")));

        // --- La libreria scritta.
        List<Shortcut> after = ShortcutLibrary.Load(new SteamUser("1", "", config, DateTime.UtcNow)).Shortcuts;
        Shortcut Get(string name) => after.Single(s => s.Name == name);
        Assert.DoesNotContain(after, s => s.Name == "Removed");                       // voce morta tolta
        Assert.Equal(Markers.Folder, Get("Kirby").DevkitGameId);                      // presa in carico (stesso app id)
        Assert.Equal(SteamAppId.ForShortcut(kirbyExe, "Kirby"), Get("Kirby").AppId);
        Assert.Equal(Path.Combine(games, "Zelda", "Zelda.exe"), Get("Zelda").ExePath); // il launcher
        Assert.Equal(Path.Combine(greedFolder, "GreedFall.exe"), Get("GreedFall").ExePath);
        Assert.DoesNotContain(after, s => s.IsPlaceholder && s.Name == "GreedFall");   // il segnaposto è andato via
        Shortcut rage = Get("RAGE 2");
        Assert.True(rage.IsPlaceholder);
        Assert.Equal("steamall-store:epic", rage.DevkitGameId);                      // segnaposto, con il suo store
        Assert.Equal("steamall:gog", Get("GreedFall").DevkitGameId);
        Assert.Equal("url.dll,FileProtocolHandler com.epicgames.launcher://apps/ns%3Ar2%3ARage2?action=launch", rage.LaunchOptions);
        Assert.Equal(3, report.Added);     // Zelda, GreedFall, RAGE 2
        Assert.Equal(2, report.Removed);   // la voce morta e il segnaposto di GreedFall

        // --- Il launcher di Zelda, con le sue impostazioni e il profilo Steam del controller.
        Assert.Equal(LauncherInstaller.Launcher, File.ReadAllBytes(Path.Combine(games, "Zelda", "Zelda.exe")));
        Dictionary<string, string> ini = LauncherConfig.Read(Path.Combine(games, "Zelda", "SteamAll", "launcher.ini"));
        Assert.Equal("Cemu.exe", ini["Emulator"]);
        Assert.Equal(@"games\Zelda.wua", ini["Game"]);
        Assert.Equal("-g \"{rom}\"", ini["Arguments"]);
        Assert.Equal(@"portable\controllerProfiles", ini["ConfigDir"]);
        Assert.True(File.Exists(Path.Combine(games, "Zelda", "SteamAll", "Steam", "controller0.xml")));

        // --- Le immagini delle voci nuove, e l'icona del segnaposto.
        string grid = Path.Combine(config, "grid");
        Assert.True(File.Exists(Path.Combine(grid, $"{rage.AppId}p.png")));
        Assert.True(File.Exists(Path.Combine(grid, $"{rage.AppId}_hero.png")));
        Assert.Equal(Path.Combine(grid, $"{rage.AppId}-icon.png"), rage.Icon);
        Assert.DoesNotContain(http.Requests, r => r.Contains("fake-key"));   // la chiave non finisce mai nell'indirizzo

        // --- Pulizia: l'immagine orfana è nel "Cestino".
        Assert.Contains(platform.RecycledFiles, f => f.EndsWith("4000000000p.png", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_NothingToChange_DoesNotCloseSteam()
    {
        string steam = temp.Folder("Steam");
        string config = temp.Folder(@"Steam\userdata\1\config");
        File.WriteAllBytes(Path.Combine(config, "shortcuts.vdf"), BinaryVdf.Write(ShortcutLibrary.EmptyLibrary()));
        var platform = new FakePlatform(steam);
        var settings = new AppSettings { GameFolders = [], Cleanup = false };
        ShortcutLibrary library = ShortcutLibrary.Load(new SteamUser("1", "", config, DateTime.UtcNow));
        ImportPlan plan = ImportPlan.Create(library, []);

        ApplyReport report = await new SteamApplier(platform, new HttpClient(SteamGridDb())).ApplyAsync(plan, settings, Path.Combine(temp.Path, "a.json"));

        Assert.Equal(0, platform.Shutdowns);
        Assert.False(report.SteamRestarted);
    }
}
