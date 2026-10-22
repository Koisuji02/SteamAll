// =====================================================================================
//  RomScanner.cs
//  Un emulatore installato nel sistema + le sue cartelle di ROM (come Steam ROM Manager): trova i
//  giochi del SUO sistema e ignora tutto il resto, anche se tutte le ROM stanno nella stessa cartella.
//    - prima l'estensione del file (Emulator.Extensions);
//    - poi, per i formati usati da più sistemi (.iso, .chd, .cso, .pbp), il contenuto del file e
//      i nomi delle cartelle (vedi RomSniffer);
//    - poi le regole del sistema: per Switch solo i giochi base (niente update e DLC), per Wii U i
//      giochi estratti solo dalla loro cartella "code", per PS3 i dischi (PS3_GAME) e i giochi
//      digitali installati, per i dischi in più parti la playlist .m3u al posto dei singoli dischi.
//  Il nome in Steam viene dal nome del file, ripulito: "Super Mario Sunshine (USA) [!].rvz" ->
//  "Super Mario Sunshine". Per PS3 e Wii U estratti si legge il titolo dentro il gioco.
// =====================================================================================

using System.Text.RegularExpressions;
using SteamAll.Core.Settings;

namespace SteamAll.Core.Games;

public static class RomScanner
{
    /// <summary>
    /// I giochi di un emulatore installato. "launcherExe" è il programma che Steam avvierà per
    /// questi giochi (serve a calcolare l'app id). Le cartelle che non esistono si saltano.
    /// </summary>
    public static List<Game> Scan(EmulatorSetup setup, string sourceName, string launcherExe)
    {
        Emulator emulator = setup.Emulator ?? throw new InvalidDataException($"unknown emulator \"{setup.EmulatorId}\"");
        var games = new List<Game>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in setup.RomFolders.Where(Directory.Exists))
        {
            foreach ((string path, string name) in FindGames(folder, emulator))
            {
                if (!seen.Add(path)) continue;   // due cartelle che si sovrappongono: una volta sola
                games.Add(new Game
                {
                    Key = $"rom:{setup.Id}:{path.ToLowerInvariant()}",
                    Name = name,
                    Source = sourceName,
                    Origin = GameOrigin.Emulator,
                    Kind = emulator.Name,
                    Emulator = emulator,
                    EmulatorExe = setup.ExePath,
                    EmulatorSetupId = setup.Id,
                    RomPath = path,
                    InstallFolder = Path.GetDirectoryName(path)!,
                    ExePath = launcherExe,
                    StartDir = Path.GetDirectoryName(launcherExe) ?? "",
                    Icon = setup.ExePath,
                });
            }
        }
        // In ordine alfabetico, come nelle cartelle dei giochi.
        return games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>I giochi di una cartella per questo emulatore: (file da aprire, nome). "internal" per le prove.</summary>
    internal static List<(string Path, string Name)> FindGames(string folder, Emulator emulator)
    {
        // Tutti i file, anche nelle sottocartelle; le cartelle che non si possono leggere si saltano.
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        List<string> files = Directory.EnumerateFiles(folder, "*", options).ToList();

        // I dischi elencati in una playlist .m3u non si aggiungono uno per uno: c'è la playlist.
        var inPlaylists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string m3u in files.Where(f => Ext(f) is ".m3u" or ".m3u8"))
        {
            foreach (string line in SafeLines(m3u).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
                inPlaylists.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(m3u)!, line)));
        }

        var result = new List<(string, string)>();
        foreach (string file in files)
        {
            string ext = Ext(file);
            if (!emulator.Extensions.Contains(ext)) continue;
            if (inPlaylists.Contains(Path.GetFullPath(file))) continue;
            if (ext is ".m3u" or ".m3u8" && !PlaylistIsFor(file, emulator)) continue;
            string? name = emulator.Id switch
            {
                "ryujinx" => SwitchGame(file),
                "cemu" => WiiUGame(file),
                "dolphin" => ext == ".iso" && RomSniffer.Iso(file) is not (DiscKind.GameCube or DiscKind.Wii) ? null : CleanName(file),
                "rpcs3" => Ps3Game(file),
                "pcsx2" => Ps2Game(file, ext),
                "duckstation" => Ps1Game(file, ext),
                "ppsspp" => PspGame(file, ext),
                "xenia" => ext == ".iso" && RomSniffer.FolderHint(file) != "x360" ? null : X360Game(file),
                _ => CleanName(file),
            };
            if (name is { Length: > 0 }) result.Add((file, name));
        }
        return result;
    }

    private static string Ext(string file) => Path.GetExtension(file).ToLowerInvariant();

    private static IEnumerable<string> SafeLines(string file)
    {
        try { return File.ReadAllLines(file); }
        catch (IOException) { return []; }
    }

    // Una playlist .m3u è di questo emulatore se elenca file delle sue estensioni.
    private static bool PlaylistIsFor(string m3u, Emulator emulator) =>
        SafeLines(m3u).Select(l => Path.GetExtension(l.Trim()).ToLowerInvariant())
                      .Any(e => e.Length > 0 && e is not (".m3u" or ".m3u8") && emulator.Extensions.Contains(e));

    // --- Le regole di ogni sistema ------------------------------------------------------

    private static string? SwitchGame(string file)
    {
        // Le cartelle "updates" e "dlc", e i nomi da update o DLC, non sono giochi base.
        string dir = (Path.GetDirectoryName(file) ?? "").ToLowerInvariant();
        if (Regex.IsMatch(dir, @"[\\/](updates?|dlcs?)([\\/]|$)")) return null;
        return GameFileFinder.IsSwitchBaseGame(Path.GetFileName(file)) ? CleanName(file) : null;
    }

    private static string? WiiUGame(string file)
    {
        if (Ext(file) != ".rpx") return CleanName(file);
        // Gioco estratto: <titolo>\code\gioco.rpx, accanto a content e meta, solo titoli base.
        DirectoryInfo? code = Directory.GetParent(file);
        DirectoryInfo? title = code?.Parent;
        if (code is null || title is null || !code.Name.Equals("code", StringComparison.OrdinalIgnoreCase)) return null;
        if (!Directory.Exists(Path.Combine(title.FullName, "content")) || !Directory.Exists(Path.Combine(title.FullName, "meta"))) return null;
        if (!Regex.IsMatch(GameFileFinder.WiiUTitleId(title.FullName), "^0005000[02]")) return null;
        string meta = Path.Combine(title.FullName, "meta", "meta.xml");
        Match m = File.Exists(meta) ? Regex.Match(File.ReadAllText(meta), @"<longname_en[^>]*>([^<]+)<") : Match.Empty;
        return m.Success ? Tidy(m.Groups[1].Value.Replace('\n', ' ')) : Tidy(title.Name);
    }

    private static string? Ps3Game(string file)
    {
        if (!Path.GetFileName(file).Equals("EBOOT.BIN", StringComparison.OrdinalIgnoreCase)) return null;
        DirectoryInfo? usrdir = Directory.GetParent(file);
        DirectoryInfo? game = usrdir?.Parent;
        if (usrdir is null || game is null || !usrdir.Name.Equals("USRDIR", StringComparison.OrdinalIgnoreCase)) return null;
        Dictionary<string, string> sfo = GameFileFinder.ReadParamSfo(Path.Combine(game.FullName, "PARAM.SFO"));
        // Un disco (PS3_GAME) o un gioco digitale installato (categoria HG); non gli update.
        bool disc = game.Name.Equals("PS3_GAME", StringComparison.OrdinalIgnoreCase);
        if (!disc && sfo.GetValueOrDefault("CATEGORY") != "HG") return null;
        string title = sfo.GetValueOrDefault("TITLE", "");
        return title.Length > 0 ? Tidy(title.Replace('\n', ' ')) : Tidy(disc ? game.Parent?.Name ?? game.Name : game.Name);
    }

    private static string? Ps2Game(string file, string ext)
    {
        string hint = RomSniffer.FolderHint(file);
        return ext switch
        {
            ".iso" => RomSniffer.Iso(file) == DiscKind.PlayStation && hint is not ("ps1" or "psp") ? CleanName(file) : null,
            ".chd" => RomSniffer.Chd(file) == DiscKind.Dvd || hint == "ps2" ? CleanName(file) : null,
            ".cso" or ".zso" => hint == "ps2" ? CleanName(file) : null,   // .cso è anche della PSP
            _ => CleanName(file),
        };
    }

    private static string? Ps1Game(string file, string ext)
    {
        string hint = RomSniffer.FolderHint(file);
        return ext switch
        {
            ".chd" => RomSniffer.Chd(file) != DiscKind.Dvd && hint is not ("ps2" or "psp") ? CleanName(file) : null,
            ".pbp" => hint != "psp" && !IsEboot(file) ? CleanName(file) : null,
            _ => CleanName(file),
        };
    }

    private static string? PspGame(string file, string ext)
    {
        string hint = RomSniffer.FolderHint(file);
        return ext switch
        {
            ".iso" => RomSniffer.Iso(file) == DiscKind.Psp ? CleanName(file) : null,
            ".cso" => hint != "ps2" ? CleanName(file) : null,
            // "when" aggiunge una condizione a un caso. Un EBOOT.PBP ha il nome della sua cartella.
            ".pbp" when IsEboot(file) => Tidy(Path.GetFileName(Path.GetDirectoryName(file)!)),
            ".pbp" or ".chd" when hint == "psp" => CleanName(file),
            ".pbp" or ".chd" => null,   // senza indizi sono della PS1
            _ => CleanName(file),
        };
    }

    private static bool IsEboot(string file) => Path.GetFileName(file).Equals("EBOOT.PBP", StringComparison.OrdinalIgnoreCase);

    // Xbox 360: default.xex ha il nome della sua cartella.
    private static string X360Game(string file) =>
        Path.GetFileName(file).Equals("default.xex", StringComparison.OrdinalIgnoreCase)
            ? Tidy(Path.GetFileName(Path.GetDirectoryName(file)!))
            : CleanName(file);

    // --- I nomi ---------------------------------------------------------------------------

    /// <summary>Il nome del gioco dal nome del file, ripulito (vedi Tidy).</summary>
    public static string CleanName(string file) => Tidy(Path.GetFileNameWithoutExtension(file));

    /// <summary>
    /// Toglie le etichette tra parentesi tonde e quadre ("(USA)", "[!]", "[0100...]", "(Rev 1)"),
    /// i "_" al posto degli spazi e gli spazi doppi. Le parentesi con il numero del disco restano:
    /// "Final Fantasy VII (Disc 1)" -> "Final Fantasy VII (Disc 1)".
    /// </summary>
    public static string Tidy(string name)
    {
        string text = name.Replace('_', ' ');
        text = Regex.Replace(text, @"\s*\[[^\]]*\]", "");
        text = Regex.Replace(text, @"\s*\((?![^)]*\b(dis[ck]|cd)\s*\d)[^)]*\)", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\s{2,}", " ").Trim();
        return text.Length > 0 ? text : name.Trim();
    }
}
