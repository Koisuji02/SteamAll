// =====================================================================================
//  SettingsTests.cs
//  Prove delle impostazioni: si salvano e si rileggono uguali, un file rovinato non blocca il
//  programma, e la chiave API nel file non si legge in chiaro.
// =====================================================================================

using System.Runtime.Versioning;
using SteamAll.Core.Platform;
using SteamAll.Core.Settings;

namespace SteamAll.Tests;

public class SettingsTests
{
    [Fact]
    public void SaveThenLoad_GivesTheSameSettings()
    {
        using var temp = new TempFolder();
        string file = Path.Combine(temp.Path, "settings.json");
        var settings = new AppSettings
        {
            GameFolders = [@"C:\Games", @"D:\More Games"],
            SteamUserId = "123456789",
            HeroHd = false,
            ExcludedGames = ["epic:abc"],
            DisabledStores = ["ea"],
            Emulators = [new EmulatorSetup { Id = "e1", EmulatorId = "dolphin", ExePath = @"C:\Dolphin\Dolphin.exe", RomFolders = [@"D:\Roms"] }],
        };

        settings.Save(file);
        AppSettings loaded = AppSettings.Load(file);

        Assert.Equal(settings.GameFolders, loaded.GameFolders);
        Assert.Equal("123456789", loaded.SteamUserId);
        Assert.False(loaded.HeroHd);
        Assert.Contains("epic:abc", loaded.ExcludedGames);
        Assert.Contains("ea", loaded.DisabledStores);
        EmulatorSetup dolphin = Assert.Single(loaded.Emulators);
        Assert.Equal([@"D:\Roms"], dolphin.RomFolders);
        Assert.Equal("-b -e \"{rom}\"", dolphin.EffectiveArguments);   // nessun parametro scelto: quelli consigliati
        Assert.DoesNotContain("EffectiveArguments", File.ReadAllText(file));   // le proprietà calcolate non si salvano
    }

    [Fact]
    public void Load_BrokenOrMissingFile_GivesTheDefaults()
    {
        using var temp = new TempFolder();
        string broken = temp.File("settings.json", "{ this is not json");

        Assert.Equal([@"C:\Games"], AppSettings.Load(broken).GameFolders);
        Assert.Equal([@"C:\Games"], AppSettings.Load(Path.Combine(temp.Path, "missing.json")).GameFolders);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void SteamGridDbKey_IsStoredEncrypted()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the key is encrypted with Windows DPAPI");
        using var temp = new TempFolder();
        string file = Path.Combine(temp.Path, "settings.json");
        var settings = new AppSettings();
        const string key = "0123456789abcdef-fake-test-key";   // una chiave finta, solo per la prova

        settings.SetSteamGridDbKey(Platforms.Current, key);
        settings.Save(file);

        Assert.DoesNotContain(key, File.ReadAllText(file));   // nel file non c'è in chiaro
        Assert.Equal(key, AppSettings.Load(file).GetSteamGridDbKey(Platforms.Current));
    }
}
