// =====================================================================================
//  ArtworkChoiceTests.cs
//  Le immagini scelte nella pagina di dettaglio: Apply le scrive al posto di quelle di prima (che
//  vanno nel Cestino), e una volta sola (il file di stato ricorda cosa ha già scritto).
// =====================================================================================

using SteamAll.Core.Artwork;

namespace SteamAll.Tests;

public class ArtworkChoiceTests : IDisposable
{
    private readonly TempFolder temp = new();
    public void Dispose() => temp.Dispose();

    [Fact]
    public async Task ChosenImage_ReplacesTheOldOne_OnlyOnce()
    {
        string grid = temp.Folder("grid");
        temp.File(@"grid\3000000001_hero.png", "old hero");
        var platform = new FakePlatform(temp.Path);
        byte[] png = FakeHttp.Png(1920, 620);
        int downloads = 0;
        var http = new FakeHttp(_ => { downloads++; return FakeHttp.Bytes(png); });
        string state = Path.Combine(temp.Path, "artwork.json");
        var job = new ArtworkJob(3_000_000_001, "Game", new Dictionary<string, string>(),
                                 Choices: new Dictionary<ArtworkKind, string> { [ArtworkKind.Hero] = "https://img.test/chosen.png" });
        // Senza chiave e senza immagini dello store, le altre immagini restano mancanti: si scarica solo quella scelta.
        ArtworkService Service() => new(new SteamGridDbClient(new HttpClient(http), ""), platform, grid, state, heroHd: true);

        ArtworkResult first = await Service().EnsureAsync(job);
        ArtworkResult second = await Service().EnsureAsync(job);

        Assert.Equal(1, first.Written);
        Assert.Equal(0, second.Written);
        Assert.Equal(1, downloads);   // la seconda volta non si riscarica (e l'hero scelto non viene "migliorato" in HD)
        Assert.Equal(png, File.ReadAllBytes(Path.Combine(grid, "3000000001_hero.png")));
        Assert.Contains(platform.RecycledFiles, f => f.EndsWith("3000000001_hero.png", StringComparison.Ordinal));   // quella vecchia: nel Cestino
    }
}
