// =====================================================================================
//  ControllerTests.cs
//  Prove del cambio del controller (lo stesso codice che gira nel launcher) e del launcher vero,
//  su cartelle di gioco FINTE: emulatori portable nella cartella del gioco ed emulatori installati.
// =====================================================================================

using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using SteamAll.Core.Controllers;
using SteamAll.Core.Games;
using SteamAll.Core.Launchers;
using SteamAll.Core.Platform;
using SteamAll.Core.Settings;
using SteamAll.Launcher;

namespace SteamAll.Tests;

public class ControllerTests : IDisposable
{
    private readonly TempFolder temp = new();
    public void Dispose() => temp.Dispose();

    private static Game EmulatedGame(string dir, string exe, string rom) => new()
    {
        Key = "folder:" + dir, Name = Path.GetFileName(dir), Source = Path.GetDirectoryName(dir)!, InstallFolder = dir,
        ExePath = Path.Combine(dir, Path.GetFileName(dir) + ".exe"), StartDir = dir, RomPath = rom,
        Emulator = Emulator.All.Single(e => e.Exe == exe), EmulatorExe = Path.Combine(dir, exe),
    };

    [Fact]
    public void DetectMode_NeedsBothSteamAndSteamInput()
    {
        // Un "dizionario" come finto ambiente: GetValueOrDefault dà null per le variabili che non ci sono.
        var fromSteam = new Dictionary<string, string> { ["SteamGameId"] = "123", ["SteamVirtualGamepadInfo"] = "x" };
        var steamWithoutInput = new Dictionary<string, string> { ["SteamGameId"] = "123" };

        Assert.Equal(ControllerSwitch.Steam, ControllerSwitch.DetectMode(n => fromSteam.GetValueOrDefault(n), out _));
        Assert.Equal(ControllerSwitch.Own, ControllerSwitch.DetectMode(n => steamWithoutInput.GetValueOrDefault(n), out _));
        Assert.Equal(ControllerSwitch.Own, ControllerSwitch.DetectMode(_ => null, out _));
    }

    [Fact]
    public void Dolphin_SwitchesBetweenSteamAndOwnSetup_KeepingYourChanges()
    {
        string dir = temp.Folder("Mario Galaxy");
        temp.File(@"Mario Galaxy\Dolphin.exe");
        temp.File(@"Mario Galaxy\portable.txt");
        string pad = temp.File(@"Mario Galaxy\User\Config\GCPadNew.ini", "[GCPad1]\nDevice = SDL/0/Joy-Con (L/R)\n");
        ControllerTarget target = ControllerSetup.ForFolderGame(EmulatedGame(dir, "Dolphin.exe", @"games\a.rvz"));
        Assert.Equal(Path.Combine(dir, "User", "Config"), target.ConfigDir);

        ControllerSetup.Prepare(target);   // mette il profilo "Steam" incluso
        Assert.True(ControllerSetup.Status(target).SteamReady);

        // Da Steam: la tua configurazione va da parte, entra quella per il controller virtuale di Steam.
        ControllerSwitch.Prepare(target.ConfigDir, target.DataDir, "Dolphin", ControllerSwitch.Steam);
        Assert.Contains("XInput", File.ReadAllText(pad));   // il profilo Steam usa XInput/0 (il pad virtuale)
        Assert.True(ControllerSetup.Status(target).OwnSaved);

        // Fuori da Steam: torna la tua, così com'era.
        ControllerSwitch.Prepare(target.ConfigDir, target.DataDir, "Dolphin", ControllerSwitch.Own);
        Assert.Equal("[GCPad1]\nDevice = SDL/0/Joy-Con (L/R)\n", File.ReadAllText(pad));
        Assert.Equal(ControllerSwitch.Own, ControllerSetup.Status(target).Active);
    }

    [Fact]
    public void Ryujinx_ReplacesOnlyTheControllerPart_OfConfigJson()
    {
        string dir = temp.Folder("Kirby");
        temp.File(@"Kirby\Ryujinx.exe");
        const string config = """
            {
              "res_scale": 2,
              "input_config": [
                {
                  "id": "0-00000005-057e-0000-0620-000000006801",
                  "player_index": "Player1",
                  "backend": "GamepadSDL2"
                }
              ],
              "player_input_assignments": [],
              "enable_vsync": true
            }
            """;
        string file = temp.File(@"Kirby\portable\Config.json", config);
        ControllerTarget target = ControllerSetup.ForFolderGame(EmulatedGame(dir, "Ryujinx.exe", @"games\k.nsp"));
        ControllerSetup.Prepare(target);

        ControllerSwitch.Prepare(target.ConfigDir, target.DataDir, "Ryujinx", ControllerSwitch.Steam);
        string text = File.ReadAllText(file);

        Assert.Contains("28de-0000-ff11", text);        // il controller virtuale di Steam
        Assert.Contains("\"res_scale\": 2", text);      // le altre impostazioni restano...
        Assert.Contains("\"enable_vsync\": true", text);
        Assert.Contains("\"player_index\": \"Player1\"", text);

        ControllerSwitch.Prepare(target.ConfigDir, target.DataDir, "Ryujinx", ControllerSwitch.Own);
        Assert.Contains("0-00000005-057e-0000-0620-000000006801", File.ReadAllText(file));   // ...e torna il Joy-Con
    }

    [Fact]
    public void Rpcs3_KeepsSubfolders_WithTheSameNamesOnEverySystem()
    {
        // RPCS3 ha file in una sottocartella (global\Default.yml): da parte si chiamano "global/Default.yml".
        string config = temp.Folder("rpcs3config");
        temp.File(@"rpcs3config\active_input_configurations.yml", "Player 1 Input: Default\n");
        temp.File(@"rpcs3config\global\Default.yml", "Player 1 Input:\n  Handler: Evdev\n");
        string data = Path.Combine(temp.Path, "data");

        ControllerSwitch.WriteSlot(Path.Combine(data, ControllerSwitch.Steam), ControllerSetup.BundledSteamProfile("RPCS3"));
        ControllerSwitch.Prepare(config, data, "RPCS3", ControllerSwitch.Steam);

        Assert.Contains("global/Default.yml", ControllerSwitch.ReadSlot(Path.Combine(data, ControllerSwitch.Own)).Keys);
        ControllerSwitch.Prepare(config, data, "RPCS3", ControllerSwitch.Own);
        Assert.Equal("Player 1 Input:\n  Handler: Evdev\n", File.ReadAllText(Path.Combine(config, "global", "Default.yml")));
    }

    [Fact]
    public void SetupBoilRData_IsTakenOver()
    {
        string dir = temp.Folder("Zelda");
        temp.File(@"Zelda\Cemu.exe");
        temp.Folder(@"Zelda\portable");
        temp.File(@"Zelda\Setup_BoilR\Fisico\controller0.xml", "<joycon/>");
        temp.File(@"Zelda\Setup_BoilR\Steam\controller0.xml", "<steam/>");
        temp.File(@"Zelda\Setup_BoilR\stato.ini", "Attivo=Fisico\r\n");
        ControllerTarget target = ControllerSetup.ForFolderGame(EmulatedGame(dir, "Cemu.exe", @"games\z.wua"));

        ControllerSetup.Prepare(target);

        Assert.Equal("<joycon/>", File.ReadAllText(Path.Combine(dir, @"SteamAll\Own\controller0.xml")));
        Assert.Equal("<steam/>", File.ReadAllText(Path.Combine(dir, @"SteamAll\Steam\controller0.xml")));   // la sua, non quella inclusa
        Assert.Equal(ControllerSwitch.Own, ControllerSetup.Status(target).Active);
        Assert.True(Directory.Exists(Path.Combine(dir, "Setup_BoilR")));   // la cartella vecchia resta, come copia di sicurezza
    }

    [Fact]
    public void ConfigLocator_InstalledEmulators()
    {
        // Un Dolphin "portable" accanto al suo programma vince sulle impostazioni del sistema.
        string exe = temp.File(@"Dolphin\Dolphin.exe");
        temp.File(@"Dolphin\portable.txt");
        Assert.Equal(Path.Combine(temp.Path, "Dolphin", "User", "Config"), ConfigLocator.ForInstalled(exe, "Dolphin", OsKind.Windows, out _));

        // Gli emulatori senza configurazione gestita (DuckStation...) si arrangiano da soli.
        Assert.Null(ConfigLocator.ForInstalled(exe, "", OsKind.Windows, out string reason));
        Assert.Contains("by itself", reason);
    }

    [Fact]
    public void LayoutCheck_FindsTwoButtonsOnTheSameKey()
    {
        const string layout = """
            "controller_mappings"
            {
                "group" { "id" "7" "mode" "four_buttons" "inputs" {
                    "button_a" { "activators" { "Full_Press" { "bindings" { "binding" "xinput_button A" } } } }
                    "button_b" { "activators" { "Full_Press" { "bindings" { "binding" "xinput_button A" } } } }
                    "button_x" { "activators" { "Full_Press" { "bindings" { "binding" "xinput_button X" } } } }
                } }
                "preset" { "group_source_bindings" { "7" "button_diamond active" } }
            }
            """;

        string problem = Assert.Single(SteamInputLayouts.DoubleBindings(layout));

        Assert.StartsWith("A and B all send A", problem);
    }

    [Theory]
    [InlineData("-b -e \"{rom}\"", "C:\\a b.iso", "-b -e \"C:\\a b.iso\"")]
    [InlineData("--fullscreen", "x.iso", "--fullscreen \"x.iso\"")]   // senza {rom}: il gioco va in fondo
    public void LauncherConfig_CommandLine(string arguments, string rom, string expected) =>
        Assert.Equal(expected, LauncherConfig.CommandLine(arguments, rom));

    [Fact]
    public void LauncherConfig_ReadsTheOldFormatToo()
    {
        var old = new Dictionary<string, string> { ["ArgsBefore"] = "-g", ["ArgsAfter"] = "" };
        Assert.Equal("-g \"{rom}\"", LauncherConfig.Arguments(old));
    }

    // Il launcher vero, avviato come lo avvierebbe Steam: con un "emulatore" finto (una copia di
    // cmd.exe che esce subito con il codice 3) controlliamo che parta, passi il gioco e scriva il registro.
    [Fact]
    [SupportedOSPlatform("windows")]
    public void Launcher_StartsTheEmulatorAndWaitsForIt()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the launcher is a Windows program");
        string dir = temp.Folder("Test Game");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), Path.Combine(dir, "Dolphin.exe"));
        temp.File(@"Test Game\games\game.iso", "game");
        Game game = EmulatedGame(dir, "Dolphin.exe", @"games\game.iso") with
        {
            // "cmd /c exit 3 <gioco>": esce con il codice 3.
            Emulator = new Emulator("fake", "Fake", "Dolphin.exe", [], [], [], "/c exit 3 \"{rom}\"", "", true),
        };

        LauncherResult installed = LauncherInstaller.Install(game, manageControllers: false, OsKind.Windows);
        using Process launcher = Process.Start(new ProcessStartInfo(game.ExePath) { UseShellExecute = false })!;
        bool finished = launcher.WaitForExit(15_000);

        Assert.Equal(LauncherState.Created, installed.State);
        Assert.True(finished);
        Assert.Equal(3, launcher.ExitCode);   // il codice di uscita dell'"emulatore" passa a Steam
        string log = File.ReadAllText(Path.Combine(dir, @"SteamAll\last start.txt"), Encoding.UTF8);
        Assert.Contains("/c exit 3", log);
        Assert.Contains(@"games\game.iso", log);
    }

    // Il launcher condiviso di un emulatore installato: il gioco arriva dalle opzioni di avvio di Steam.
    [Fact]
    [SupportedOSPlatform("windows")]
    public void SharedLauncher_GetsTheGameFromSteam()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the launcher is a Windows program");
        string emulator = Path.Combine(temp.Folder("Emu"), "fake-emu.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), emulator);
        string rom = temp.File(@"Roms\My Game.iso", "game");
        string previous = LauncherInstaller.LaunchersRoot;
        LauncherInstaller.LaunchersRoot = Path.Combine(temp.Path, "launchers");   // non nella cartella vera di SteamAll
        try
        {
            var setup = new EmulatorSetup { Id = "test1", EmulatorId = "pcsx2", ExePath = emulator, Arguments = "/c exit 4 \"{rom}\"" };
            LauncherResult installed = LauncherInstaller.InstallSetup(setup, manageControllers: true, OsKind.Windows);
            using Process launcher = Process.Start(new ProcessStartInfo(LauncherInstaller.SharedLauncherExe("test1"), $"\"{rom}\"") { UseShellExecute = false })!;
            bool finished = launcher.WaitForExit(15_000);

            Assert.Equal(LauncherState.Created, installed.State);
            Assert.True(finished);
            Assert.Equal(4, launcher.ExitCode);
            string log = File.ReadAllText(Path.Combine(LauncherInstaller.SetupHome("test1"), @"SteamAll\last start.txt"), Encoding.UTF8);
            Assert.Contains(rom, log);
        }
        finally
        {
            LauncherInstaller.LaunchersRoot = previous;
        }
    }
}
