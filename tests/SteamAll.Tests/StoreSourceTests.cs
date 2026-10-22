// =====================================================================================
//  StoreSourceTests.cs
//  Prove degli store (Epic, GOG GALAXY, Ubisoft Connect) con dati FINTI scritti qui sotto, nello
//  stesso formato dei file veri, più due prove sui tuoi store veri (in sola lettura) che vengono
//  saltate se lo store non c'è.
// =====================================================================================

using System.Runtime.Versioning;   // SupportedOSPlatform
using System.Text;
using System.Text.Json.Nodes;
using SteamAll.Core;
using SteamAll.Core.Games;
using SteamAll.Core.Platform;
using SteamAll.Core.Sources;
using SteamAll.Core.Steam;
using SteamAll.Core.Stores;

namespace SteamAll.Tests;

public class StoreSourceTests
{
    // ---------------------------------------------------------------------------------
    //  Epic
    // ---------------------------------------------------------------------------------

    // Un pezzo di catalogo come quello della cache di Epic. """...""" è una stringa grezza: il JSON
    // si scrive così com'è, virgolette comprese.
    private const string EpicCatalog = """
        [
          { "id": "c1", "namespace": "ns1", "title": "Ghostrunner?",
            "categories": [ { "path": "games" } ],
            "releaseInfo": [ { "appId": "Ghost", "platform": [ "Windows" ] } ],
            "keyImages": [ { "type": "DieselGameBoxTall", "url": "https://img/tall.jpg" } ] },
          { "id": "c2", "namespace": "ns1", "title": "Ghostrunner Demo",
            "categories": [ { "path": "games" } ],
            "releaseInfo": [ { "appId": "GhostDemo", "platform": [ "Windows" ] } ] },
          { "id": "c3", "namespace": "ns2", "title": "Some DLC", "mainGameItem": { "id": "c9" },
            "categories": [ { "path": "games" } ],
            "releaseInfo": [ { "appId": "Dlc", "platform": [ "Windows" ] } ] },
          { "id": "c4", "namespace": "ns3", "title": "Mac Only",
            "categories": [ { "path": "games" } ],
            "releaseInfo": [ { "appId": "Mac", "platform": [ "Mac" ] } ] },
          { "id": "c5", "namespace": "ns4", "title": "Rocket League",
            "categories": [ { "path": "games" } ],
            "releaseInfo": [ { "appId": "Sugar", "platform": [ "Windows" ] } ] }
        ]
        """;

    [Fact]
    public void Epic_KeepsOnlyWindowsGames_WithoutDlcOrExtras()
    {
        JsonNode catalog = JsonNode.Parse(EpicCatalog)!;
        var installed = new Dictionary<string, EpicInstall> { ["c5"] = new(@"D:\Epic\RocketLeague", @"D:\Epic\RocketLeague\RL.exe") };

        List<Game> games = EpicSource.ParseCatalog(catalog, installed);

        // Restano Ghostrunner (senza il "?") e Rocket League: la demo, il DLC e il gioco per Mac no.
        Assert.Equal(["Ghostrunner", "Rocket League"], games.Select(g => g.Name));

        Game ghost = games[0];
        Assert.False(ghost.IsInstalled);
        Assert.Equal("com.epicgames.launcher://apps/ns1%3Ac1%3AGhost?action=launch", ghost.StoreUri);
        Assert.Equal("https://img/tall.jpg", ghost.StoreImages["p"]);
        Assert.Equal("epic", ghost.StoreId);

        Game rocket = games[1];
        Assert.True(rocket.IsInstalled);
        Assert.Equal(@"D:\Epic\RocketLeague", rocket.InstallFolder);
    }

    [Fact]
    public void Epic_ReadManifests_IgnoresIncompleteInstalls()
    {
        using var temp = new TempFolder();
        string game = temp.Folder("Games\\Rocket");
        // Nei JSON le barre \ si scrivono doppie: Replace le raddoppia.
        string folder = game.Replace(@"\", @"\\");
        temp.File(@"Manifests\a.item", $$"""{ "CatalogItemId": "c5", "InstallLocation": "{{folder}}" }""");
        temp.File(@"Manifests\b.item", $$"""{ "CatalogItemId": "c6", "InstallLocation": "{{folder}}", "bIsIncompleteInstall": true }""");
        temp.File(@"Manifests\c.item", """{ "CatalogItemId": "c7", "InstallLocation": "Z:\\missing" }""");
        // $$"""...""" = stringa grezza interpolata: i valori si mettono tra {{ }}, le { } singole restano JSON.

        Dictionary<string, EpicInstall> installed = EpicSource.ReadManifests(Path.Combine(temp.Path, "Manifests"));

        Assert.Equal(game, Assert.Single(installed).Value.Folder);   // solo c5: c6 è a metà, c7 non ha la cartella
    }

    // ---------------------------------------------------------------------------------
    //  GOG GALAXY
    // ---------------------------------------------------------------------------------

    // Una riga come quelle della query: releaseKey, title, originalTitle, meta, images, isDlc, visibile, nascosto.
    private static string?[] Row(string key, string title, string meta = """{"developers":["Dev"]}""",
                                 string? dlc = "0", string? visible = "1", string? hidden = null, string? images = null) =>
        [key, $$"""{"title":"{{title}}"}""", null, meta, images, dlc, visible, hidden];

    [Fact]
    public void Gog_KeepsOnlyGogGames_NotThoseOfLinkedPlatforms()
    {
        string?[][] rows =
        [
            Row("gog_1", "GreedFall", images: """{"verticalCover":"https://gog/c.webp"}"""),
            Row("uplay_2", "Far Cry 3"),                        // Ubisoft: lo legge Ubisoft Connect, non GALAXY
            Row("steam_3", "Portal"),                           // Steam: già in Steam
            Row("gog_4", "GreedFall DLC", dlc: "1"),            // DLC
            Row("gog_5", "Hidden Game", hidden: "1"),           // nascosto in GALAXY
            Row("gog_6", "Invisible", visible: "0"),            // doppione che GOG non mostra
            Row("gog_7", "Soundtrack", meta: """{"developers":[],"genres":[]}"""),   // contenuto extra
        ];
        var installed = new Dictionary<string, string> { ["gog_1"] = @"C:\GOG Games\GreedFall" };

        List<Game> games = GogSource.FromRows(rows, installed);

        Game greedFall = Assert.Single(games);
        Assert.Equal("GreedFall", greedFall.Name);
        Assert.Equal("gog", greedFall.StoreId);
        Assert.True(greedFall.IsInstalled);
        Assert.Equal("https://gog/c.png", greedFall.StoreImages["p"]);   // WebP -> PNG
    }

    // ---------------------------------------------------------------------------------
    //  Ubisoft Connect: la cache protobuf con le configurazioni YAML
    // ---------------------------------------------------------------------------------

    private const string FarCry5Yaml = """
        version: 2.0
        root:
          name: l1
          thumb_image: fc5_thumb.jpg
          background_image: fc5_bg.jpg
          start_game:
            online:
              executables:
              - path:
                  relative: bin\FarCry5.exe
                working_directory:
                  register: HKEY_LOCAL_MACHINE\SOFTWARE\Ubisoft\Launcher\Installs\42\InstallDir
          addons:
          - id: 777
            is_visible: yes
        localizations:
          default:
            l1: Far Cry® 5
        """;

    // Un piccolo "scrittore" di protobuf, il contrario di Protobuf.Read: serve a costruire una cache finta.
    private static byte[] Varint(ulong v)
    {
        var bytes = new List<byte>();
        do
        {
            byte b = (byte)(v & 0x7F);
            v >>= 7;
            if (v != 0) b |= 0x80;
            bytes.Add(b);
        } while (v != 0);
        return [.. bytes];
    }

    private static byte[] NumberField(int n, ulong v) => [.. Varint((ulong)(n << 3)), .. Varint(v)];
    private static byte[] BytesField(int n, byte[] data) => [.. Varint((ulong)(n << 3 | 2)), .. Varint((ulong)data.Length), .. data];

    private static byte[] CacheGame(ulong uplayId, string yaml) =>
        BytesField(1, [.. NumberField(1, uplayId), .. NumberField(2, uplayId), .. BytesField(3, Encoding.UTF8.GetBytes(yaml))]);

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Ubisoft_ReadsTheGamesYouOwn_FromTheLocalCache()
    {
        const string steamVersion = "root:\n  name: Steam Copy\n  start_game:\n    online: {}\n  third_party_platform:\n    name: steam\n";
        const string ulc = "root:\n  name: Season Pass\n  is_ulc: yes\n  start_game:\n    offline:\n      x: 1\n";
        const string addon = "root:\n  name: The Addon\n  start_game:\n    offline:\n      x: 1\n";
        const string notAGame = "root:\n  name: Tool\n";
        byte[] cache = [.. CacheGame(42, FarCry5Yaml), .. CacheGame(1, steamVersion), .. CacheGame(2, ulc), .. CacheGame(777, addon), .. CacheGame(3, notAGame)];

        List<UbisoftStore.UbisoftProduct> products = UbisoftStore.ParseCache(cache);

        UbisoftStore.UbisoftProduct farCry = Assert.Single(products);   // solo il gioco vero
        Assert.Equal("42", farCry.UplayId);
        Assert.Equal("Far Cry 5", farCry.Name);                          // tradotto da "l1", senza ®
        Assert.Equal("https://ubistatic3-a.akamaihd.net/orbit/uplay_launcher_3_0/assets/fc5_thumb.jpg", farCry.Images["p"]);
        Assert.EndsWith("fc5_bg.jpg", farCry.Images["hero"]);
    }

    [Fact]
    public void MiniYaml_FlattensPathsAndLists()
    {
        Dictionary<string, List<string>> y = MiniYaml.Flatten(FarCry5Yaml + "\nquoted: 'It''s'\n");

        Assert.Equal("l1", y.First("root.name"));
        Assert.Equal(@"bin\FarCry5.exe", y.First("root.start_game.online.executables[].path.relative"));
        Assert.Contains("777", y["root.addons[].id"]);
        Assert.Equal("yes", y.First("root.addons[].is_visible"));
        Assert.Equal("Far Cry® 5", y.First("localizations.default.l1"));
        Assert.Equal("It's", y.First("quoted"));
        Assert.True(y.Has("root.start_game"));
        Assert.False(y.Has("root.third_party_platform"));
    }

    // ---------------------------------------------------------------------------------
    //  Lo store di una voce già in Steam (per togliere quelle degli store spenti)
    // ---------------------------------------------------------------------------------

    private static Shortcut Entry(string exe, string options, string marker) =>
        new(Shortcut.CreateNode(3_000_000_000, "x", $"\"{exe}\"", "", "", options, marker, true));

    [Theory]
    [InlineData("C:\\x.exe", "", "steamall:ubisoft", "ubisoft")]
    [InlineData("rundll32.exe", "url.dll,FileProtocolHandler uplay://install/4", "steamall-store:ubisoft", "ubisoft")]
    [InlineData("rundll32.exe", "url.dll,FileProtocolHandler goggalaxy://openGameView/uplay_4", "setupboilr-store", "ubisoft")]
    [InlineData("rundll32.exe", "url.dll,FileProtocolHandler goggalaxy://openGameView/gog_4", "setupboilr-store", "gog")]
    [InlineData("Launcher.exe", "com.epicgames.launcher://apps/a%3Ab%3Ac?action=launch", "boilr", "epic")]
    [InlineData("heroic", "--no-gui \"heroic://launch/nile/x\"", "boilr", "amazon")]
    [InlineData("C:\\Games\\Kirby\\Kirby.exe", "", "steamall:folder", "folder")]
    public void StoreCatalog_IdentifiesTheStoreOfAnEntry(string exe, string options, string marker, string expected) =>
        Assert.Equal(expected, StoreCatalog.Identify(Entry(exe, options, marker)));

    [Fact]
    public void Markers_RecogniseOldAndNewEntries()
    {
        Assert.True(Markers.IsPlaceholder("steamall-store:epic"));
        Assert.True(Markers.IsPlaceholder("setupboilr-store"));
        Assert.False(Markers.IsPlaceholder("steamall:epic"));
        Assert.True(Markers.IsSteamAll("steamall"));
        Assert.True(Markers.IsSteamAll("steamall:emulator"));
        Assert.False(Markers.IsSteamAll("boilr"));
        Assert.Equal("epic", Markers.StoreOf("steamall-store:epic"));
        Assert.Null(Markers.StoreOf("steamall"));
    }

    // ---------------------------------------------------------------------------------
    //  Sui tuoi store veri (sola lettura): saltate se lo store non è installato
    // ---------------------------------------------------------------------------------

    // [SupportedOSPlatform]: dice al compilatore che questa prova chiama codice solo-Windows (su un altro
    // sistema la prima riga la salta comunque).
    [Fact]
    [SupportedOSPlatform("windows")]
    public void RealEpic_ReadsTheLibrary()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Epic is read on Windows only");
        List<Game>? games = EpicSource.Scan(Platforms.Current);
        Assert.SkipWhen(games is null, "Epic Games is not installed on this PC");

        Assert.NotEmpty(games!);
        Assert.All(games!, g => Assert.StartsWith("com.epicgames.launcher://apps/", g.StoreUri));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void RealGog_ReadsTheLibrary()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "GOG GALAXY is read on Windows only");
        List<Game>? games = GogSource.Scan(Platforms.Current);
        Assert.SkipWhen(games is null, "GOG GALAXY is not installed on this PC");

        Assert.NotEmpty(games!);
        Assert.All(games!, g => Assert.StartsWith("goggalaxy://openGameView/gog_", g.StoreUri));
    }
}
