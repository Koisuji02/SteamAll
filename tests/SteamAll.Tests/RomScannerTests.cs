// =====================================================================================
//  RomScannerTests.cs
//  Prove degli emulatori installati con le cartelle di ROM: una cartella FINTA con le ROM di tanti
//  sistemi mescolate, e ogni emulatore deve prendere solo le sue. I file finti hanno le stesse
//  "firme" di quelli veri (i numeri magici di GameCube, i descrittori ISO 9660 di PS2 e PSP...).
// =====================================================================================

using System.Text;
using SteamAll.Core.Games;
using SteamAll.Core.Platform;
using SteamAll.Core.Settings;
using SteamAll.Core.Sources;

namespace SteamAll.Tests;

public class RomScannerTests : IDisposable
{
    private readonly TempFolder temp = new();
    public void Dispose() => temp.Dispose();

    // Un file di "size" byte con questi byte a queste posizioni (il resto zeri).
    private string Binary(string relative, int size, params (int Offset, byte[] Bytes)[] parts)
    {
        var data = new byte[size];
        foreach ((int offset, byte[] bytes) in parts) bytes.CopyTo(data, offset);
        string path = Path.Combine(temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return path;
    }

    // Un disco ISO 9660 con questo identificativo di sistema ("PLAYSTATION", "PSP GAME").
    private string Iso9660(string relative, string system) =>
        Binary(relative, 0x8800, (0x8000, [1, .. "CD001"u8.ToArray()]), (0x8008, Encoding.ASCII.GetBytes(system.PadRight(32))));

    // Un PARAM.SFO minimo (il file con le informazioni di un gioco PS3), con CATEGORY e TITLE.
    private string ParamSfo(string relative, string category, string title)
    {
        byte[] keys = [.. "CATEGORY\0TITLE\0"u8.ToArray()];
        byte[] cat = Encoding.UTF8.GetBytes(category + "\0"), tit = Encoding.UTF8.GetBytes(title + "\0");
        int keyTable = 20 + 2 * 16, dataTable = keyTable + keys.Length;
        var b = new List<byte>();
        b.AddRange([0x00, 0x50, 0x53, 0x46]);                        // "\0PSF"
        b.AddRange(BitConverter.GetBytes(0x0101u));                  // versione
        b.AddRange(BitConverter.GetBytes((uint)keyTable));
        b.AddRange(BitConverter.GetBytes((uint)dataTable));
        b.AddRange(BitConverter.GetBytes(2u));                       // 2 voci
        void Entry(ushort keyOffset, byte[] value, uint dataOffset)
        {
            b.AddRange(BitConverter.GetBytes(keyOffset));
            b.AddRange(BitConverter.GetBytes((ushort)0x0204));       // testo UTF-8
            b.AddRange(BitConverter.GetBytes((uint)value.Length));
            b.AddRange(BitConverter.GetBytes((uint)value.Length));
            b.AddRange(BitConverter.GetBytes(dataOffset));
        }
        Entry(0, cat, 0);
        Entry(9, tit, (uint)cat.Length);
        b.AddRange(keys);
        b.AddRange(cat);
        b.AddRange(tit);
        string path = Path.Combine(temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [.. b]);
        return path;
    }

    private List<string> NamesFor(string emulatorId) =>
        RomScanner.FindGames(Path.Combine(temp.Path, "Roms"), Emulator.Find(emulatorId)!).Select(g => g.Name).Order().ToList();

    [Fact]
    public void EachEmulator_TakesOnlyTheGamesOfItsSystem()
    {
        // Switch: il gioco base, non l'update né la cartella degli update.
        temp.File(@"Roms\Kirby [01004D300C5AE000].nsp", "base");
        temp.File(@"Roms\Kirby [01004D300C5AE800][v65536].nsp", "update");
        temp.File(@"Roms\updates\Kirby Update.nsp", "update");
        // GameCube (numero magico a 0x1C), Wii in .rvz, PS2 e PSP in .iso.
        Binary(@"Roms\Wind Waker (USA).iso", 0x40, (0x1C, [0xC2, 0x33, 0x9F, 0x3D]));
        temp.File(@"Roms\Mario Galaxy (Europe) (En,Fr).rvz", "wii");
        Iso9660(@"Roms\God of War (USA).iso", "PLAYSTATION");
        Iso9660(@"Roms\Patapon (USA).iso", "PSP GAME");
        // PS1 in due dischi con la playlist: va solo la playlist.
        temp.File(@"Roms\FF7\Final Fantasy VII (Disc 1).cue", "cue");
        temp.File(@"Roms\FF7\Final Fantasy VII (Disc 2).cue", "cue");
        temp.File(@"Roms\FF7\Final Fantasy VII.m3u", "Final Fantasy VII (Disc 1).cue\nFinal Fantasy VII (Disc 2).cue\n");
        // PS3: un disco con il suo titolo dentro PARAM.SFO; e un .bin che non è un gioco PS3.
        temp.File(@"Roms\Demons\PS3_GAME\USRDIR\EBOOT.BIN", "elf");
        ParamSfo(@"Roms\Demons\PS3_GAME\PARAM.SFO", "DG", "Demon's Souls");
        temp.File(@"Roms\FF7\track.bin", "audio");
        // DS e GBA: basta l'estensione.
        temp.File(@"Roms\Pokemon Black (USA).nds", "ds");
        temp.File(@"Roms\Metroid Fusion [!].gba", "gba");

        Assert.Equal(["Kirby"], NamesFor("ryujinx"));
        Assert.Equal(["Mario Galaxy", "Wind Waker"], NamesFor("dolphin"));
        Assert.Equal(["God of War"], NamesFor("pcsx2"));
        Assert.Equal(["Patapon"], NamesFor("ppsspp"));
        Assert.Equal(["Final Fantasy VII"], NamesFor("duckstation"));
        Assert.Equal(["Demon's Souls"], NamesFor("rpcs3"));
        Assert.Equal(["Pokemon Black"], NamesFor("melonds"));
        Assert.Equal(["Metroid Fusion"], NamesFor("mgba"));
        Assert.Empty(NamesFor("cemu"));
    }

    [Fact]
    public void Scan_MakesGamesForTheSharedLauncher()
    {
        string rom = temp.File(@"Roms\Wind Waker.rvz", "gc");
        var setup = new EmulatorSetup { Id = "abc", EmulatorId = "dolphin", ExePath = @"C:\Dolphin\Dolphin.exe", RomFolders = [Path.Combine(temp.Path, "Roms")] };

        Game game = Assert.Single(RomScanner.Scan(setup, "Dolphin (GameCube/Wii)", @"C:\Launchers\abc\SteamAll Launcher.exe"));

        Assert.Equal(GameOrigin.Emulator, game.Origin);
        Assert.Equal(rom, game.RomPath);
        Assert.Equal("abc", game.EmulatorSetupId);
        Assert.Equal(@"C:\Launchers\abc\SteamAll Launcher.exe", game.ExePath);
        Assert.Equal(@"C:\Dolphin\Dolphin.exe", game.Icon);   // l'icona dell'emulatore
    }

    [Theory]
    [InlineData("Super Mario Sunshine (USA) [!]", "Super Mario Sunshine")]
    [InlineData("Final Fantasy VII (Disc 1)", "Final Fantasy VII (Disc 1)")]
    [InlineData("zelda_twilight_princess", "zelda twilight princess")]
    [InlineData("(USA)", "(USA)")]   // se resta vuoto, il nome originale
    public void Tidy_CleansFileNames(string name, string expected) => Assert.Equal(expected, RomScanner.Tidy(name));

    [Fact]
    public void Emulator_IsRecognisedFromItsProgram()
    {
        Assert.Equal("dolphin", Emulator.Recognize(@"C:\Emu\Dolphin.exe")?.Id);
        Assert.Equal("pcsx2", Emulator.Recognize("/usr/bin/pcsx2-qt")?.Id);
        Assert.Equal("duckstation", Emulator.Recognize(@"D:\DuckStation\duckstation-qt-x64-ReleaseLTCG.exe")?.Id);
        Assert.Null(Emulator.Recognize(@"C:\Windows\notepad.exe"));
    }

    [Fact]
    public void GamesFolder_OnLinux_RecognisesEmulatorsAndPcGames()
    {
        temp.File(@"Games\Mario\dolphin-emu", "emulator");
        temp.File(@"Games\Mario\games\mario.rvz", "game");
        temp.File(@"Games\Celeste\Celeste.sh", "game");

        var byName = GamesFolderSource.Scan(Path.Combine(temp.Path, "Games"), OsKind.Linux, "/opt/SteamAll").ToDictionary(g => g.Name);

        Assert.Equal("Dolphin (GameCube/Wii)", byName["Mario"].Kind);
        Assert.Equal("/opt/SteamAll", byName["Mario"].ExePath);   // lo avvia SteamAll ("--launch")
        Assert.Equal(Path.Combine(temp.Path, "Games", "Mario", "dolphin-emu"), byName["Mario"].EmulatorExe);
        Assert.Equal("PC game", byName["Celeste"].Kind);
        Assert.EndsWith("Celeste.sh", byName["Celeste"].ExePath);
    }
}
