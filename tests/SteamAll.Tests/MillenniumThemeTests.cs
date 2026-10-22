// =====================================================================================
//  MillenniumThemeTests.cs
//  Prove del tema consigliato portato da SteamAll (installazione in uno Steam FINTO) e della lettura
//  delle impostazioni di Steam Input.
// =====================================================================================

using System.Text.Json.Nodes;
using SteamAll.Core.Controllers;
using SteamAll.Core.Millennium;

namespace SteamAll.Tests;

public class MillenniumThemeTests : IDisposable
{
    private readonly TempFolder temp = new();
    public void Dispose() => temp.Dispose();

    [Fact]
    public void Bundle_HasTheTheme_WithoutTheOldInstallButtonSection()
    {
        Dictionary<string, byte[]> files = ThemeInstaller.BundledFiles();

        Assert.True(files.ContainsKey("skin.json"));
        Assert.True(files.ContainsKey("libraryroot.custom.css"));
        string js = System.Text.Encoding.UTF8.GetString(files["libraryroot.custom.js"]);
        Assert.DoesNotContain("STORE GAMES NOT INSTALLED", js);   // ora lo fa il plugin di SteamAll
        Assert.Contains("WINDOWS ACCENT COLOR", js);
    }

    [Fact]
    public void Install_CopiesTheTheme_MakesItActive_AndRecyclesTheOldCopy()
    {
        string steam = temp.Folder("Steam");
        temp.File(@"Steam\millennium\config\config.json", """{ "themes": { "activeTheme": "Other", "allowedScripts": true } }""");
        temp.File(@"Steam\millennium\themes\Steam2.0-modular-WindowsAccent\old.css", "old");
        var platform = new FakePlatform(steam);
        Assert.Equal(ThemeState.Outdated, ThemeInstaller.Status(steam));

        ThemeInstaller.Install(steam, platform);

        Assert.Equal(ThemeState.Installed, ThemeInstaller.Status(steam));
        Assert.True(ThemeInstaller.IsActive(steam));
        JsonNode config = JsonNode.Parse(File.ReadAllText(Path.Combine(steam, @"millennium\config\config.json")))!;
        Assert.True(config["themes"]!["allowedScripts"]!.GetValue<bool>());   // il resto resta com'era
        Assert.Contains(platform.RecycledFiles, f => f.EndsWith("Steam2.0-modular-WindowsAccent", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(steam, @"millennium\config\config.json.steamall-backup")));
    }

    [Fact]
    public void WithoutMillennium_SaysSo() =>
        Assert.Equal(ThemeState.MillenniumMissing, ThemeInstaller.Status(temp.Folder("Steam")));

    [Fact]
    public void SteamInput_ReadsThePerControllerSettings()
    {
        const string localConfig = """
            "UserLocalConfigStore"
            {
                "SteamController_XBoxSupport"		"1"
                "SteamController_SwitchSupport"		"1"
                "SteamController_GenericGamepadSupport"		"0"
            }
            """;

        Assert.True(SteamInputSettings.IsEnabled(localConfig, ControllerFamily.Xbox));
        Assert.True(SteamInputSettings.IsEnabled(localConfig, ControllerFamily.Nintendo));
        Assert.False(SteamInputSettings.IsEnabled(localConfig, ControllerFamily.Generic));
        Assert.Null(SteamInputSettings.IsEnabled(localConfig, ControllerFamily.PlayStation));   // non scritto: vale il predefinito di Steam
        Assert.Equal(ControllerFamily.PlayStation, SteamInputSettings.FamilyOf(0x054C));
        Assert.Equal(ControllerFamily.Generic, SteamInputSettings.FamilyOf(0x2DC8));   // 8BitDo in modalità "generica"
    }
}
