// =====================================================================================
//  PlanTests.cs
//  Prove del piano: per ogni caso (gioco nuovo, già in Steam, da togliere, doppione, segnaposto,
//  store spento) costruiamo una libreria di Steam FINTA e delle fonti FINTE, e controlliamo lo stato.
// =====================================================================================

using SteamAll.Core;
using SteamAll.Core.Games;
using SteamAll.Core.Plan;
using SteamAll.Core.Settings;
using SteamAll.Core.Steam;

namespace SteamAll.Tests;

public class PlanTests : IDisposable
{
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    // --- Aiuti per costruire i dati finti --------------------------------------------------

    // Una libreria di Steam con queste voci, scritta in una cartella "userdata" finta e riletta.
    // "params" = si possono passare quante voci si vuole, separate da virgole.
    private ShortcutLibrary Library(params VdfNode[] shortcuts)
    {
        VdfNode root = ShortcutLibrary.EmptyLibrary();
        VdfNode list = root.GetNode("shortcuts")!;
        for (int i = 0; i < shortcuts.Length; i++) list.Entries.Add(VdfEntry.FromMap(i.ToString(), shortcuts[i]));
        string config = temp.Folder(@"userdata\1\config");
        File.WriteAllBytes(Path.Combine(config, "shortcuts.vdf"), BinaryVdf.Write(root));
        return ShortcutLibrary.Load(new SteamUser("1", "", config, DateTime.UtcNow));
    }

    // Una voce di BoilR per un exe (che creiamo davvero, se exists è true: le voci con l'exe sparito
    // sono "morte").
    private VdfNode BoilR(string name, string exe, bool exists = true, string options = "")
    {
        if (exists) temp.File(Path.GetRelativePath(temp.Path, exe));
        return Shortcut.CreateNode(SteamAppId.ForShortcut(exe, name), name, $"\"{exe}\"", "", "", options, "boilr", true);
    }

    // Una voce nostra, con il suo marchio (l'exe lo creiamo davvero).
    private VdfNode Ours(string name, string marker, string options = "", uint appId = 3_100_000_000)
    {
        string exe = temp.File(@"Launchers\" + name + ".exe");
        return Shortcut.CreateNode(appId, name, $"\"{exe}\"", "", "", options, marker, true);
    }

    private static VdfNode Placeholder(string name, string uri, uint appId, string marker = Markers.SetupBoilRPlaceholder) =>
        Shortcut.CreateNode(appId, name, "\"C:\\WINDOWS\\System32\\rundll32.exe\"", "", "",
                            "url.dll,FileProtocolHandler " + uri, marker, false);

    private static VdfNode ByHand(string name) =>
        Shortcut.CreateNode(3_000_000_000, name, "\"C:\\Windows\\notepad.exe\"", "", "", "", "", true);

    private Game FolderGame(string name) => new()
    {
        Key = "folder:" + name,
        Name = name,
        Source = temp.Path,
        ExePath = Path.Combine(temp.Path, name, name + ".exe"),
    };

    private static Game StoreGame(string store, string name, string uri, bool installed = false) => new()
    {
        Key = uri,
        Name = name,
        Source = store,
        Origin = GameOrigin.Store,
        StoreId = store,
        ProviderId = store,
        Kind = store,
        IsInstalled = installed,
        StoreUri = uri,
        InstallFolder = installed ? @"C:\Store\" + name : "",
    };

    private static SourceResult Folder(string path, params Game[] games) => new(path, SourceKind.Folder, Markers.FolderStore, [.. games]);
    // "[.. games]" = una lista con tutti gli elementi di games (lo "spread" di una collection expression).

    private static SourceResult Store(string id, params Game[] games) => new(id, SourceKind.Store, id, [.. games]);

    // Lo stato della riga con questo nome.
    private static SteamStatus StatusOf(ImportPlan plan, string name) => plan.Entries.Single(e => e.Game.Name == name).Status;

    // --- Le prove --------------------------------------------------------------------------

    [Fact]
    public void FolderGames_AreNewOrInSteam()
    {
        string kirbyExe = Path.Combine(temp.Path, "Kirby", "Kirby.exe");
        ShortcutLibrary library = Library(BoilR("Kirby", kirbyExe));

        ImportPlan plan = ImportPlan.Create(library, [Folder(temp.Path, FolderGame("Kirby"), FolderGame("Zelda"))]);

        Assert.Equal(SteamStatus.InSteam, StatusOf(plan, "Kirby"));   // stesso app id della voce di BoilR
        Assert.Equal(SteamStatus.New, StatusOf(plan, "Zelda"));
    }

    [Fact]
    public void Unchecking_RemovesOnlyOurEntries()
    {
        string kirbyExe = Path.Combine(temp.Path, "Kirby", "Kirby.exe");
        ShortcutLibrary library = Library(BoilR("Kirby", kirbyExe), ByHand("Mine"));
        ImportPlan plan = ImportPlan.Create(library, [Folder(temp.Path, FolderGame("Kirby"), FolderGame("Mine"))]);

        foreach (PlanEntry e in plan.Entries) e.IsChecked = false;

        Assert.Equal(SteamStatus.WillBeRemoved, StatusOf(plan, "Kirby"));   // voce di BoilR: si toglie
        Assert.Equal(SteamStatus.InSteam, StatusOf(plan, "Mine"));          // aggiunta a mano: resta
    }

    [Fact]
    public void NotInstalledStoreGames_BecomeOrStayPlaceholders()
    {
        ShortcutLibrary library = Library(Placeholder("GreedFall", "goggalaxy://openGameView/gog_1", 2_878_101_719));

        ImportPlan plan = ImportPlan.Create(library,
        [
            Store("gog",
                StoreGame("gog", "GreedFall", "goggalaxy://openGameView/gog_1"),
                StoreGame("gog", "Dishonored 2", "goggalaxy://openGameView/gog_2")),
        ]);

        Assert.Equal(SteamStatus.InSteam, StatusOf(plan, "GreedFall"));   // il suo segnaposto c'è già
        Assert.Equal(SteamStatus.New, StatusOf(plan, "Dishonored 2"));
        Assert.DoesNotContain(plan.Entries, e => e.Game.Source == ImportPlan.LeftoversSource);
    }

    [Fact]
    public void OldGalaxyPlaceholder_IsTakenOverByItsOwnStore()
    {
        // Setup_BoilR apriva GOG GALAXY anche per i giochi Ubisoft: ora il segnaposto apre Ubisoft Connect,
        // ma resta la stessa voce di Steam (stesso app id: immagini e raccolte restano).
        ShortcutLibrary library = Library(Placeholder("Far Cry 5", "goggalaxy://openGameView/uplay_42", 2_900_000_001));

        ImportPlan plan = ImportPlan.Create(library, [Store("ubisoft", StoreGame("ubisoft", "Far Cry® 5", "uplay://install/42"))],
            new Dictionary<string, StoreStatus> { ["ubisoft"] = StoreStatus.Active });

        PlanEntry farCry = Assert.Single(plan.Entries);
        Assert.Equal(SteamStatus.InSteam, farCry.Status);
        Assert.Equal(2_900_000_001u, farCry.AppId);
    }

    [Fact]
    public void UninstalledGame_ItsEntryBecomesThePlaceholder()
    {
        ShortcutLibrary library = Library(Ours("Anno 1800", Markers.For("ubisoft"), "uplay://launch/5/0"));

        ImportPlan plan = ImportPlan.Create(library, [Store("ubisoft", StoreGame("ubisoft", "Anno 1800", "uplay://install/5"))]);

        PlanEntry anno = Assert.Single(plan.Entries);
        Assert.Equal(SteamStatus.InSteam, anno.Status);   // la stessa voce, che diventerà il segnaposto
        Assert.Contains("becomes an Install placeholder", anno.Note);
    }

    [Fact]
    public void NotInstalledStoreGame_AlreadyInSteamByName_IsSkipped()
    {
        ShortcutLibrary library = Library(ByHand("Ghostrunner™"));

        ImportPlan plan = ImportPlan.Create(library, [Store("epic", StoreGame("epic", "Ghostrunner", "com.epicgames.launcher://apps/x"))]);

        PlanEntry ghost = plan.Entries.Single(e => e.Game.Source == "epic");
        Assert.Equal(SteamStatus.Skipped, ghost.Status);
        Assert.Contains("already in Steam", ghost.Problem);
    }

    [Fact]
    public void InstalledStoreGame_ReplacesItsPlaceholder()
    {
        ShortcutLibrary library = Library(Placeholder("GreedFall", "goggalaxy://openGameView/gog_1", 2_878_101_719));

        ImportPlan plan = ImportPlan.Create(library,
            [Store("gog", StoreGame("gog", "GreedFall", "goggalaxy://openGameView/gog_1", installed: true))]);

        PlanEntry greedFall = Assert.Single(plan.Entries);   // nessuna voce rimasta: il segnaposto è "suo"
        Assert.Equal(SteamStatus.New, greedFall.Status);
        Assert.NotNull(greedFall.ReplacedPlaceholder);

        greedFall.IsChecked = false;
        Assert.Equal(SteamStatus.WillBeRemoved, greedFall.Status);   // senza spunta, anche il segnaposto va via
    }

    [Fact]
    public void SameGameInTwoStores_CountsOnce_InstalledFirst()
    {
        ImportPlan plan = ImportPlan.Create(null,
        [
            Store("gog", StoreGame("gog", "The Witcher 3", "goggalaxy://openGameView/gog_3")),
            Store("epic", StoreGame("epic", "The Witcher® 3", "com.epicgames.launcher://apps/w3", installed: true)),
        ]);

        Assert.Equal(SteamStatus.New, plan.Entries.Single(e => e.Game.Source == "epic").Status);
        PlanEntry gog = plan.Entries.Single(e => e.Game.Source == "gog");
        Assert.Equal(SteamStatus.Skipped, gog.Status);
        Assert.Equal("Same game as in epic (installed)", gog.Problem);
    }

    [Fact]
    public void Leftovers_AreRemovedOnlyWhenTheirGameIsGone()
    {
        ShortcutLibrary library = Library(
            BoilR("Removed Game", Path.Combine(temp.Path, "Removed", "Removed.exe"), exists: false),   // exe sparito
            BoilR("Still There", Path.Combine(temp.Path, "Other", "Other.exe")),                         // exe presente
            Placeholder("Old Epic Game", "com.epicgames.launcher://apps/old", 1),                       // non più in Epic
            Placeholder("Old GOG Game", "goggalaxy://openGameView/gog_9", 2));                          // GOG non letto

        ImportPlan plan = ImportPlan.Create(library,
        [
            Store("epic"),
            new SourceResult("GOG GALAXY", SourceKind.Store, "gog", Games: null, Error: "database locked"),
        ]);

        Assert.Equal(SteamStatus.WillBeRemoved, StatusOf(plan, "Removed Game"));
        Assert.Equal(SteamStatus.InSteam, StatusOf(plan, "Still There"));
        Assert.Equal(SteamStatus.WillBeRemoved, StatusOf(plan, "Old Epic Game"));
        Assert.Equal(SteamStatus.Skipped, StatusOf(plan, "Old GOG Game"));   // il suo store non si è letto: resta
        Assert.All(plan.Entries, e => Assert.Equal(ImportPlan.LeftoversSource, e.Game.Source));
    }

    [Fact]
    public void StoreTurnedOffOrNotInstalled_AllItsEntriesAreRemoved()
    {
        // Come BoilR con gli store che non ci sono più: anche i segnaposto dei giochi da installare.
        ShortcutLibrary library = Library(
            Ours("Fortnite", Markers.For("epic"), "com.epicgames.launcher://apps/ns%3Ac%3AFortnite?action=launch&silent=true", 3_100_000_001),
            Placeholder("Rage 2", "com.epicgames.launcher://apps/r2", 3_100_000_002, Markers.ForPlaceholder("epic")),
            Placeholder("Far Cry 3", "goggalaxy://openGameView/uplay_2", 3_100_000_003),   // Ubisoft tramite GALAXY
            BoilR("Kirby", Path.Combine(temp.Path, "Kirby", "Kirby.exe")));                   // altro: resta

        ImportPlan plan = ImportPlan.Create(library, [],
            new Dictionary<string, StoreStatus> { ["epic"] = StoreStatus.Disabled, ["ubisoft"] = StoreStatus.NotInstalled });

        Assert.Equal(SteamStatus.WillBeRemoved, StatusOf(plan, "Fortnite"));
        Assert.Equal(SteamStatus.WillBeRemoved, StatusOf(plan, "Rage 2"));
        Assert.Equal(SteamStatus.WillBeRemoved, StatusOf(plan, "Far Cry 3"));
        Assert.Contains("turned off", plan.Entries.Single(e => e.Game.Name == "Rage 2").Note);
        Assert.Contains("not installed", plan.Entries.Single(e => e.Game.Name == "Far Cry 3").Note);
        Assert.Equal(SteamStatus.InSteam, StatusOf(plan, "Kirby"));
    }

    [Fact]
    public void InstalledGamesOnly_RemovesThePlaceholders()
    {
        // Con "solo giochi installati" i segnaposto già in Steam si tolgono (anche se lo store non si legge).
        ShortcutLibrary library = Library(
            Placeholder("Rage 2", "com.epicgames.launcher://apps/r2", 3_100_000_002, Markers.ForPlaceholder("epic")),
            Placeholder("GreedFall", "goggalaxy://openGameView/gog_1", 3_100_000_003));

        ImportPlan plan = ImportPlan.Create(library, [Store("epic"), new SourceResult("GOG GALAXY", SourceKind.Store, "gog", null, "locked")],
            new Dictionary<string, StoreStatus> { ["epic"] = StoreStatus.Active, ["gog"] = StoreStatus.Active }, includeNotInstalled: false);

        Assert.All(plan.Entries, e => Assert.Equal(SteamStatus.WillBeRemoved, e.Status));
        Assert.Contains("installed games only", plan.Entries[0].Note);
    }

    [Fact]
    public void OurStoreEntry_NoLongerListed_IsRemoved_ButOnlyIfTheStoreWasRead()
    {
        VdfNode anno = Ours("Anno 1800", Markers.For("ubisoft"), "uplay://launch/5/0");

        ImportPlan read = ImportPlan.Create(Library(anno), [Store("ubisoft")],
            new Dictionary<string, StoreStatus> { ["ubisoft"] = StoreStatus.Active });
        ImportPlan failed = ImportPlan.Create(Library(anno), [new SourceResult("Ubisoft Connect", SourceKind.Store, "ubisoft", null, "broken cache")],
            new Dictionary<string, StoreStatus> { ["ubisoft"] = StoreStatus.Active });

        Assert.Equal(SteamStatus.WillBeRemoved, StatusOf(read, "Anno 1800"));
        Assert.Equal(SteamStatus.Skipped, StatusOf(failed, "Anno 1800"));
    }

    [Fact]
    public void EmulatorGames_SameAsAFolderGame_AreSkipped_AndOldOnesRemoved()
    {
        Game rom(string name) => new()
        {
            Key = "rom:x:" + name, Name = name, Source = "Dolphin (GameCube/Wii)", Origin = GameOrigin.Emulator,
            ExePath = @"C:\Launchers\x\SteamAll Launcher.exe", RomPath = $@"D:\Roms\{name}.rvz", EmulatorSetupId = "x",
        };
        ShortcutLibrary library = Library(Ours("Old Rom Game", Markers.Emulator, "\"D:\\Roms\\old.rvz\""));

        ImportPlan plan = ImportPlan.Create(library,
        [
            Folder(temp.Path, FolderGame("Mario Galaxy")),
            new SourceResult("Dolphin (GameCube/Wii)", SourceKind.Emulator, "x", [rom("Mario Galaxy"), rom("Wind Waker")]),
        ]);

        Assert.Equal(SteamStatus.Skipped, plan.Entries.Single(e => e.Game.Origin == GameOrigin.Emulator && e.Game.Name == "Mario Galaxy").Status);
        Assert.Equal(SteamStatus.New, StatusOf(plan, "Wind Waker"));
        Assert.Equal(SteamStatus.WillBeRemoved, StatusOf(plan, "Old Rom Game"));   // non più nelle cartelle di ROM
    }

    [Fact]
    public void ApplySelection_UsesTheSavedChoices()
    {
        ImportPlan plan = ImportPlan.Create(null,
        [
            Folder(temp.Path, FolderGame("Kirby"), FolderGame("Zelda")),
            Folder(@"D:\Other", new Game { Key = "folder:other", Name = "Other", Source = @"D:\Other", ExePath = @"D:\Other\Other\Other.exe" }),
            Store("epic", StoreGame("epic", "Rage 2", "com.epicgames.launcher://apps/r2")),
        ]);
        var settings = new AppSettings { ExcludedGames = ["folder:Zelda"], ExcludedSources = [@"D:\Other"] };

        plan.ApplySelection(settings);

        Assert.Equal(SteamStatus.New, StatusOf(plan, "Kirby"));
        Assert.Equal(SteamStatus.NotAdded, StatusOf(plan, "Zelda"));    // senza spunta
        Assert.Equal(SteamStatus.NotAdded, StatusOf(plan, "Other"));    // cartella senza spunta
        Assert.Equal(SteamStatus.New, StatusOf(plan, "Rage 2"));        // gli store si spengono nella pagina Stores
    }

    [Theory]
    [InlineData("Pokémon™ Sword", "pokemonsword")]
    [InlineData("The Witcher® 3: Wild Hunt", "thewitcher3wildhunt")]
    [InlineData("", "")]
    public void Names_Normalize(string name, string expected) => Assert.Equal(expected, Names.Normalize(name));
}
