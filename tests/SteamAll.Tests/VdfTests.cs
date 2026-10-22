// =====================================================================================
//  VdfTests.cs
//  Prove della lettura e scrittura di shortcuts.vdf. La regola più importante: leggere e
//  riscrivere un file senza modifiche deve dare ESATTAMENTE gli stessi byte.
// =====================================================================================

using SteamAll.Core;            // Files
using SteamAll.Core.Platform;   // Platforms
using SteamAll.Core.Steam;      // BinaryVdf, Shortcut...

namespace SteamAll.Tests;

public class VdfTests
{
    // Una libreria finta con due giochi, costruita in memoria (non tocca nessun file).
    private static byte[] SampleLibrary()
    {
        VdfNode root = ShortcutLibrary.EmptyLibrary();
        VdfNode list = root.GetNode("shortcuts")!;
        list.Entries.Add(VdfEntry.FromMap("0", Shortcut.CreateNode(
            3000000001, "Kirby", "\"C:\\Games\\Kirby\\Kirby.exe\"", "\"C:\\Games\\Kirby\"", "", "", "boilr", true)));
        list.Entries.Add(VdfEntry.FromMap("1", Shortcut.CreateNode(
            3000000002, "Pokémon Sword", "\"C:\\x.exe\"", "\"C:\\\"", "", "-fullscreen", Markers.SteamAll, false)));
        return BinaryVdf.Write(root);
    }

    [Fact]
    public void ReadThenWrite_GivesTheSameBytes()
    {
        byte[] original = SampleLibrary();

        byte[] again = BinaryVdf.Write(BinaryVdf.Read(original));

        Assert.Equal(original, again);   // confronta gli array byte per byte
    }

    [Fact]
    public void Read_FindsTheFieldsOfEachGame()
    {
        VdfNode list = BinaryVdf.Read(SampleLibrary()).GetNode("shortcuts")!;

        var second = new Shortcut(list.Entries[1].Child!);

        Assert.Equal(3000000002u, second.AppId);
        Assert.Equal("Pokémon Sword", second.Name);   // le lettere accentate restano intatte (UTF-8)
        Assert.Equal(@"C:\x.exe", second.ExePath);    // senza virgolette
        Assert.Equal("-fullscreen", second.LaunchOptions);
        Assert.True(second.IsSteamAll);
    }

    [Fact]
    public void Read_TruncatedFile_Throws()
    {
        byte[] data = SampleLibrary();
        byte[] cut = data[..(data.Length - 5)];   // "..x" = dall'inizio fino a x (escluso): togliamo gli ultimi 5 byte

        // Assert.Throws: la prova passa solo se il codice tra parentesi lancia quell'errore.
        Assert.Throws<InvalidDataException>(() => BinaryVdf.Read(cut));
    }

    // Prova sul TUO shortcuts.vdf vero, in sola lettura: se su questo PC Steam non c'è (per esempio
    // su un altro computer), la prova viene "saltata" invece di fallire.
    [Fact]
    public void RealLibrary_ReadThenWrite_GivesTheSameBytes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Steam is read from the Windows registry");
        string? steamFolder = Platforms.Current.FindSteamFolder();
        Assert.SkipWhen(steamFolder is null, "Steam is not installed on this PC");
        SteamUser? user = new SteamInstall(steamFolder!).ChooseUser(null);
        Assert.SkipWhen(user is null, "Steam has no users on this PC");
        string file = Path.Combine(user!.ConfigFolder, "shortcuts.vdf");
        Assert.SkipUnless(File.Exists(file), "this Steam user has no non-Steam games");

        byte[] original = Files.ReadShared(file);
        byte[] again = BinaryVdf.Write(BinaryVdf.Read(original));

        Assert.Equal(original, again);
    }
}
