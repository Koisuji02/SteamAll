// =====================================================================================
//  LauncherInstaller.cs
//  Mette (o aggiorna) i launcher dei giochi emulati, e prepara i dati del controller (vedi
//  ControllerSetup). Due casi:
//
//  1. Gioco di una cartella (emulatore portable + gioco):
//       <Gioco>\<Gioco>.exe              il launcher (lo stesso per tutti i giochi; solo Windows)
//       <Gioco>\SteamAll\launcher.ini    cosa deve avviare (emulatore, file del gioco, parametri)
//     Sostituisce anche i vecchi launcher di Setup_BoilR, riprendendo le loro configurazioni.
//  2. Emulatore installato + cartelle di ROM: un launcher solo per l'emulatore, in
//       <dati locali>\SteamAll\launchers\<id>\SteamAll Launcher.exe   (solo Windows)
//       <dati locali>\SteamAll\launchers\<id>\SteamAll\launcher.ini
//     Il gioco da aprire glielo passa Steam nelle opzioni di avvio.
//  Su Linux e macOS il launcher è SteamAll stesso ("SteamAll --launch <launcher.ini>"): si scrive
//  solo launcher.ini.
// =====================================================================================

using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using SteamAll.Core.Controllers;
using SteamAll.Core.Games;
using SteamAll.Core.Platform;
using SteamAll.Core.Settings;
using SteamAll.Launcher;

namespace SteamAll.Core.Launchers;

public enum LauncherState { UpToDate, Created, Updated, Failed }

/// <summary>Com'è andata: lo stato e una spiegazione (per il registro).</summary>
public sealed record LauncherResult(LauncherState State, string Message);

public static class LauncherInstaller
{
    /// <summary>Il nome del launcher condiviso di un emulatore installato.</summary>
    public const string SharedLauncherName = "SteamAll Launcher.exe";

    // I byte del launcher, letti una volta sola dalla risorsa incorporata nel nucleo.
    // Lazy<T> = il valore si calcola solo la prima volta che serve (e poi resta).
    private static readonly Lazy<byte[]> LauncherBytes = new(() =>
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SteamAll.Launcher.exe")
            ?? throw new InvalidOperationException("the game launcher is missing from SteamAll (build problem)");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    });

    /// <summary>Il launcher come lo porta SteamAll (per confronti e prove).</summary>
    public static byte[] Launcher => LauncherBytes.Value;

    /// <summary>
    /// La cartella dei launcher degli emulatori installati: &lt;dati locali&gt;\SteamAll\launchers.
    /// Si può cambiare solo per le prove (così non scrivono nella cartella vera).
    /// </summary>
    public static string LaunchersRoot { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamAll", "launchers");

    /// <summary>La cartella del launcher di un emulatore installato.</summary>
    public static string SetupHome(string setupId) => Path.Combine(LaunchersRoot, setupId);

    /// <summary>launcher.ini di un emulatore installato.</summary>
    public static string SetupConfigFile(string setupId) =>
        Path.Combine(SetupHome(setupId), ControllerSwitch.DataFolder, LauncherConfig.FileName);

    /// <summary>launcher.ini di un gioco di una cartella.</summary>
    public static string FolderConfigFile(Game game) =>
        Path.Combine(game.InstallFolder, ControllerSwitch.DataFolder, LauncherConfig.FileName);

    // ---------------------------------------------------------------------------------
    //  1. Giochi delle cartelle
    // ---------------------------------------------------------------------------------

    /// <summary>Lo stato del launcher di un gioco, senza cambiare niente (per l'anteprima e la scheda Controllers).</summary>
    public static LauncherState Check(Game game, bool manageControllers, OsKind os)
    {
        if (game.Emulator is null) return LauncherState.UpToDate;
        bool iniOk = SameText(FolderConfigFile(game), FolderConfigText(game, manageControllers));
        if (os != OsKind.Windows) return iniOk ? LauncherState.UpToDate : File.Exists(FolderConfigFile(game)) ? LauncherState.Updated : LauncherState.Created;
        bool exeOk = File.Exists(game.ExePath) && SameBytes(game.ExePath, Launcher);
        if (exeOk && iniOk) return LauncherState.UpToDate;
        return File.Exists(game.ExePath) ? LauncherState.Updated : LauncherState.Created;
    }

    /// <summary>Mette o aggiorna il launcher. Non tocca mai un exe che non è nostro.</summary>
    public static LauncherResult Install(Game game, bool manageControllers, OsKind os)
    {
        if (game.Emulator is null) return new LauncherResult(LauncherState.UpToDate, "");
        try
        {
            bool windows = os == OsKind.Windows;
            bool existed = windows ? File.Exists(game.ExePath) : File.Exists(FolderConfigFile(game));
            if (windows && existed && !Sources.GamesFolderSource.IsOurLauncher(game.ExePath))
                return new LauncherResult(LauncherState.Failed, $"{Path.GetFileName(game.ExePath)} is not a SteamAll launcher: left as is");

            // Prima i dati del controller (anche quelli di Setup_BoilR), poi il launcher che li usa.
            if (manageControllers) ControllerSetup.Prepare(ControllerSetup.ForFolderGame(game));

            bool changed = WriteTextIfDifferent(FolderConfigFile(game), FolderConfigText(game, manageControllers));
            if (windows)
            {
                LauncherResult? failed = WriteLauncherExe(game.ExePath, ref changed);
                if (failed is not null) return failed;
            }
            return Outcome(changed, existed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new LauncherResult(LauncherState.Failed, ex.Message);
        }
    }

    // Il contenuto di launcher.ini per un gioco di una cartella: percorsi relativi alla cartella del gioco.
    private static string FolderConfigText(Game game, bool manageControllers)
    {
        Emulator emulator = game.Emulator!;
        ControllerTarget target = ControllerSetup.ForFolderGame(game);
        string dir = game.InstallFolder;
        string configDir = manageControllers && target.ConfigDir.Length > 0 ? Path.GetRelativePath(dir, target.ConfigDir) : "";
        return LauncherConfig.Format(Path.GetRelativePath(dir, game.EmulatorExe.Length > 0 ? game.EmulatorExe : Path.Combine(dir, emulator.Exe)),
                                     game.RomPath, emulator.Arguments, configDir.Length > 0 ? emulator.ControllerKind : "",
                                     configDir, ControllerSwitch.DataFolder);
    }

    // ---------------------------------------------------------------------------------
    //  2. Emulatori installati
    // ---------------------------------------------------------------------------------

    /// <summary>Il programma che Steam avvia per i giochi di questo emulatore installato (Windows: il launcher condiviso).</summary>
    public static string SharedLauncherExe(string setupId) => Path.Combine(SetupHome(setupId), SharedLauncherName);

    public static LauncherState CheckSetup(EmulatorSetup setup, bool manageControllers, OsKind os)
    {
        string ini = SetupConfigFile(setup.Id);
        bool iniOk = SameText(ini, SetupConfigText(setup, manageControllers, os));
        bool exeOk = os != OsKind.Windows || (File.Exists(SharedLauncherExe(setup.Id)) && SameBytes(SharedLauncherExe(setup.Id), Launcher));
        if (iniOk && exeOk) return LauncherState.UpToDate;
        return File.Exists(ini) ? LauncherState.Updated : LauncherState.Created;
    }

    public static LauncherResult InstallSetup(EmulatorSetup setup, bool manageControllers, OsKind os)
    {
        try
        {
            if (!File.Exists(setup.ExePath)) return new LauncherResult(LauncherState.Failed, $"emulator not found: {setup.ExePath}");
            bool existed = File.Exists(SetupConfigFile(setup.Id));
            if (manageControllers) ControllerSetup.Prepare(ControllerSetup.ForSetup(setup, os));
            bool changed = WriteTextIfDifferent(SetupConfigFile(setup.Id), SetupConfigText(setup, manageControllers, os));
            if (os == OsKind.Windows)
            {
                LauncherResult? failed = WriteLauncherExe(SharedLauncherExe(setup.Id), ref changed);
                if (failed is not null) return failed;
            }
            return Outcome(changed, existed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new LauncherResult(LauncherState.Failed, ex.Message);
        }
    }

    // launcher.ini di un emulatore installato: percorsi completi, il gioco lo passa Steam.
    private static string SetupConfigText(EmulatorSetup setup, bool manageControllers, OsKind os)
    {
        ControllerTarget target = ControllerSetup.ForSetup(setup, os);
        bool managed = manageControllers && target.ConfigDir.Length > 0;
        return LauncherConfig.Format(setup.ExePath, "", setup.EffectiveArguments, managed ? target.Kind : "",
                                     managed ? target.ConfigDir : "", ControllerSwitch.DataFolder);
    }

    // ---------------------------------------------------------------------------------
    //  Aiuti
    // ---------------------------------------------------------------------------------

    // Scrive il launcher se manca o è diverso. Se il gioco è aperto l'exe è "in uso" e Windows
    // non lo lascia sostituire: errore chiaro. "ref bool changed": il metodo può cambiarla.
    private static LauncherResult? WriteLauncherExe(string path, ref bool changed)
    {
        if (File.Exists(path) && SameBytes(path, Launcher)) return null;
        try { ControllerSwitch.WriteFile(path, Launcher); }
        catch (IOException) { return new LauncherResult(LauncherState.Failed, "the game is running: close it and apply again"); }
        catch (UnauthorizedAccessException) { return new LauncherResult(LauncherState.Failed, "the game is running: close it and apply again"); }
        changed = true;
        return null;
    }

    private static LauncherResult Outcome(bool changed, bool existed)
    {
        if (!changed) return new LauncherResult(LauncherState.UpToDate, "launcher up to date");
        return existed ? new LauncherResult(LauncherState.Updated, "launcher updated")
                       : new LauncherResult(LauncherState.Created, "launcher created");
    }

    private static bool SameText(string file, string text) => File.Exists(file) && File.ReadAllText(file, Encoding.UTF8) == text;

    private static bool WriteTextIfDifferent(string file, string text)
    {
        if (SameText(file, text)) return false;
        ControllerSwitch.WriteFile(file, new UTF8Encoding(false).GetBytes(text));
        return true;
    }

    private static bool SameBytes(string file, byte[] expected)
    {
        var info = new FileInfo(file);
        if (info.Length != expected.Length) return false;
        // Confrontiamo i "riassunti": se sono uguali, i file sono uguali.
        return SHA256.HashData(File.ReadAllBytes(file)).AsSpan().SequenceEqual(SHA256.HashData(expected));
    }
}
