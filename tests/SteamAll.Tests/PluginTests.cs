// =====================================================================================
//  PluginTests.cs
//  Prove del plugin Millennium: installazione in una cartella di Steam FINTA, e le prove in
//  JavaScript del plugin (tests\plugin), lanciate con Node.js se è installato.
// =====================================================================================

using System.Diagnostics;
using System.Text.Json.Nodes;
using SteamAll.Core.Millennium;

namespace SteamAll.Tests;

public class PluginTests : IDisposable
{
    private readonly TempFolder temp = new();
    public void Dispose() => temp.Dispose();

    [Fact]
    public void Bundle_HasTheThreePluginFiles()
    {
        Dictionary<string, byte[]> files = PluginInstaller.BundledFiles();

        // I percorsi con "/" su tutti i sistemi (Path.Combine li accetta anche su Windows).
        Assert.True(files.ContainsKey("plugin.json"));
        Assert.True(files.ContainsKey("backend/main.lua"));
        Assert.True(files.ContainsKey(".millennium/Dist/index.js"));
    }

    [Fact]
    public void Install_CopiesAndEnables_KeepingTheOtherPlugins()
    {
        string steam = temp.Folder("Steam");
        temp.File(@"Steam\millennium\config\config.json", """{ "general": { "injectJavascript": true }, "plugins": { "enabledPlugins": ["non-steam-playtimes"] } }""");
        Assert.Equal(PluginState.NotInstalled, PluginInstaller.Status(steam));

        PluginInstaller.Install(steam);

        Assert.Equal(PluginState.Installed, PluginInstaller.Status(steam));
        JsonNode config = JsonNode.Parse(File.ReadAllText(Path.Combine(steam, @"millennium\config\config.json")))!;
        Assert.Equal(["non-steam-playtimes", PluginInstaller.PluginName],
                     config["plugins"]!["enabledPlugins"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.True(config["general"]!["injectJavascript"]!.GetValue<bool>());   // il resto della configurazione resta
        Assert.True(File.Exists(Path.Combine(steam, @"millennium\config\config.json.steamall-backup")));
    }

    [Fact]
    public void WithoutMillennium_SaysSo()
    {
        Assert.Equal(PluginState.MillenniumMissing, PluginInstaller.Status(temp.Folder("Steam")));
    }

    // Le prove JavaScript del plugin (tests\plugin\install-button.test.js) con Node.js.
    [Fact]
    public void JavaScriptTests_Pass()
    {
        string? root = FindRepositoryRoot();
        Assert.SkipWhen(root is null, "repository not found");
        var start = new ProcessStartInfo("node", "--test tests/plugin/install-button.test.js")
        {
            WorkingDirectory = root!, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        Process? node;
        try { node = Process.Start(start); }
        catch (System.ComponentModel.Win32Exception) { node = null; }   // Node.js non installato
        Assert.SkipWhen(node is null, "Node.js is not installed");

        string output = node!.StandardOutput.ReadToEnd() + node.StandardError.ReadToEnd();
        node.WaitForExit();
        Assert.True(node.ExitCode == 0, output);   // se fallisce, il messaggio è l'output di Node
    }

    // La cartella del repository: si risale da quella delle prove finché c'è SteamAll.slnx.
    private static string? FindRepositoryRoot()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "SteamAll.slnx"))) return d.FullName;
        return null;
    }
}
