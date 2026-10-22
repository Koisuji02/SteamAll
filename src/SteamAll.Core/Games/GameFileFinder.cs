// =====================================================================================
//  GameFileFinder.cs
//  Per un gioco emulato: quale file deve aprire l'emulatore (la rom, l'immagine del disco...).
//  Di solito sta in <cartella del gioco>\games; ogni emulatore ha i suoi formati e le sue regole.
//  Se non trova niente, o trova troppo, lancia un errore con la spiegazione: il gioco viene saltato.
// =====================================================================================

using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SteamAll.Core.Games;

/// <summary>Il file scelto (relativo alla cartella del gioco) e una nota facoltativa.</summary>
public sealed record GameFile(string RelativePath, string Note = "");

public static class GameFileFinder
{
    /// <summary>Il file del gioco per questo emulatore. Errore (InvalidDataException) se non si trova.</summary>
    public static GameFile Find(string gameDir, Emulator emulator) => emulator.ControllerKind switch
    {
        "Ryujinx" => FindSwitch(gameDir),
        "Cemu" => FindWiiU(gameDir),
        "Dolphin" => FindDolphin(gameDir),
        "RPCS3" => FindPs3(gameDir),
        _ => throw new InvalidDataException($"no game file rules for {emulator.Name}"),
    };

    // I file della cartella "games" (e sottocartelle) con una di queste estensioni, in ordine.
    private static List<FileInfo> GameFiles(string gameDir, params string[] extensions)
    {
        var dir = new DirectoryInfo(Path.Combine(gameDir, "games"));
        if (!dir.Exists) return [];
        return dir.EnumerateFiles("*", SearchOption.AllDirectories)
                  .Where(f => extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                  .OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
                  .ToList();
    }

    private static string Relative(string gameDir, FileInfo file) => Path.GetRelativePath(gameDir, file.FullName);

    // Più candidati: prende il più grande (di solito il gioco completo) e lo dice nella nota.
    private static GameFile Biggest(string gameDir, List<FileInfo> files, string note)
    {
        FileInfo chosen = files.MaxBy(f => f.Length)!;
        if (files.Count > 1) note = $"{files.Count} possible files, chose the biggest ({chosen.Name})" + (note.Length > 0 ? "; " + note : "");
        return new GameFile(Relative(gameDir, chosen), note);
    }

    // --- Switch (Ryujinx): .nsp / .xci, ma non gli update e i DLC ---------------------------
    private static GameFile FindSwitch(string gameDir)
    {
        List<FileInfo> files = GameFiles(gameDir, ".nsp", ".xci");
        if (files.Count == 0) throw new InvalidDataException("no .nsp or .xci file in the games folder");

        // Update e DLC già collegati in Ryujinx (portable\games\<title id>\updates.json e dlc.json).
        var linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string meta = Path.Combine(gameDir, "portable", "games");
        if (Directory.Exists(meta))
        {
            foreach (string json in Directory.EnumerateFiles(meta, "*.json", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(json);
                if (name is not ("updates.json" or "dlc.json")) continue;
                JsonNode? data = JsonHelpers.TryParse(File.ReadAllText(json, Encoding.UTF8));
                IEnumerable<string> paths = name == "updates.json"
                    ? data.Items("paths").Select(p => p.AsText())
                    : data.Items().Select(d => d.Text("path"));
                foreach (string p in paths.Where(p => p.Length > 0)) linked.Add(Path.GetFileName(p));
            }
        }

        // Il gioco base: non collegato come update/DLC, e con un nome da gioco base (vedi IsSwitchBaseGame).
        List<FileInfo> baseGames = files.Where(f => !linked.Contains(f.Name) && IsSwitchBaseGame(f.Name)).ToList();
        if (baseGames.Count == 0)
            throw new InvalidDataException($"can't find the base game: only updates/DLC in games? ({string.Join(", ", files.Select(f => f.Name))})");
        if (baseGames.Count > 1)
        {
            List<FileInfo> withId = baseGames.Where(f => Regex.IsMatch(f.Name, @"\[[0-9A-Fa-f]{13}000\]")).ToList();
            if (withId.Count > 0) baseGames = withId;
        }
        return Biggest(gameDir, baseGames, "");
    }

    private static readonly Regex SwitchTitleId = new(@"\[([0-9A-Fa-f]{16})\]");
    private static readonly Regex SwitchUpdateWords = new(@"(?i)\b(update|upd|patch|dlc|add-?on)\b|expansion pass|\[v[1-9]\d*\]");

    /// <summary>
    /// Il file Switch sembra il gioco base? Il title id (se c'è nel nome, tra [ ]) finisce in 000
    /// (update e DLC hanno altre cifre finali), e nel nome non ci sono parole da update o DLC.
    /// </summary>
    public static bool IsSwitchBaseGame(string fileName)
    {
        Match tid = SwitchTitleId.Match(fileName);
        return !(tid.Success && !tid.Groups[1].Value.EndsWith("000", StringComparison.Ordinal))
            && !SwitchUpdateWords.IsMatch(Path.GetFileNameWithoutExtension(fileName));
    }

    // --- Wii U (Cemu): .wua, poi immagini disco, poi gioco estratto (code\*.rpx) ------------
    private static GameFile FindWiiU(string gameDir)
    {
        List<FileInfo> wua = GameFiles(gameDir, ".wua");
        if (wua.Count > 0) return Biggest(gameDir, wua, ".wua archive (game, update and DLC together)");

        List<FileInfo> disc = GameFiles(gameDir, ".wux", ".wud", ".iso");
        if (disc.Count > 0) return Biggest(gameDir, disc, "disc image: install updates and DLC in Cemu");

        // Gioco estratto: code\*.rpx accanto a content e meta, solo titoli base (title id 00050000 o 00050002).
        List<FileInfo> rpx = GameFiles(gameDir, ".rpx").Where(f =>
        {
            DirectoryInfo? title = f.Directory?.Parent;
            return f.Directory!.Name.Equals("code", StringComparison.OrdinalIgnoreCase) && title is not null
                && Directory.Exists(Path.Combine(title.FullName, "content"))
                && Directory.Exists(Path.Combine(title.FullName, "meta"))
                && Regex.IsMatch(WiiUTitleId(title.FullName), "^0005000[02]");
        }).ToList();
        if (rpx.Count > 0) return Biggest(gameDir, rpx, "extracted game: install updates and DLC in Cemu");

        List<FileInfo> other = GameFiles(gameDir, ".wuhb", ".rpx", ".elf");
        if (other.Count > 0) return Biggest(gameDir, other, "");
        throw new InvalidDataException(@"no Wii U game (.wua, .wux, .wud, .iso or code\*.rpx) in the games folder");
    }

    /// <summary>Il title id di un gioco Wii U estratto (da meta\meta.xml), o "".</summary>
    internal static string WiiUTitleId(string titleDir)
    {
        string meta = Path.Combine(titleDir, "meta", "meta.xml");
        if (!File.Exists(meta)) return "";
        Match m = Regex.Match(File.ReadAllText(meta), @"<title_id[^>]*>\s*([0-9A-Fa-f]{16})\s*<");
        return m.Success ? m.Groups[1].Value : "";
    }

    // --- GameCube / Wii (Dolphin): playlist .m3u, poi un disco solo ------------------------
    private static GameFile FindDolphin(string gameDir)
    {
        List<FileInfo> m3u = GameFiles(gameDir, ".m3u", ".m3u8");
        if (m3u.Count > 0) return Biggest(gameDir, m3u, "multi-disc playlist");

        List<FileInfo> discs = GameFiles(gameDir, ".rvz", ".iso", ".gcm", ".wbfs", ".ciso", ".gcz", ".wia", ".tgc", ".nfs");
        if (discs.Count == 1) return new GameFile(Relative(gameDir, discs[0]));
        if (discs.Count > 1)
        {
            List<FileInfo> disc1 = discs.Where(f => Regex.IsMatch(Path.GetFileNameWithoutExtension(f.Name), @"(?i)\b(disc|disk|disco)\s*1\b")).ToList();
            if (disc1.Count == 1)
                return new GameFile(Relative(gameDir, disc1[0]), "several discs: starts from disc 1 (make an .m3u file for automatic disc changes)");
            throw new InvalidDataException($"several games/discs in games ({string.Join(", ", discs.Select(f => f.Name))}): keep one or make an .m3u file");
        }

        List<FileInfo> other = GameFiles(gameDir, ".wad", ".dol", ".elf");
        if (other.Count > 0) return Biggest(gameDir, other, "");
        throw new InvalidDataException("no GameCube/Wii game (.rvz, .iso, .wbfs, .gcz, ...) in the games folder");
    }

    // --- PS3 (RPCS3): disco (PS3_GAME\USRDIR\EBOOT.BIN o .iso), poi gioco digitale installato ----
    private static GameFile FindPs3(string gameDir)
    {
        var discs = new List<FileInfo>();
        string games = Path.Combine(gameDir, "games");
        if (Directory.Exists(games))
        {
            discs.AddRange(new DirectoryInfo(games).EnumerateFiles("EBOOT.BIN", SearchOption.AllDirectories)
                .Where(f => f.Directory!.Name.Equals("USRDIR", StringComparison.OrdinalIgnoreCase)
                         && (f.Directory.Parent?.Name.Equals("PS3_GAME", StringComparison.OrdinalIgnoreCase) ?? false)));
        }
        discs.AddRange(GameFiles(gameDir, ".iso"));
        if (discs.Count == 1) return new GameFile(Relative(gameDir, discs[0]), "disc game: the update installed in RPCS3 is applied automatically");
        if (discs.Count > 1) throw new InvalidDataException($"several PS3 games in games ({discs.Count}): keep one per folder");

        // Gioco digitale (PSN) installato in RPCS3: dev_hdd0\game\<ID> con CATEGORY = HG nel PARAM.SFO.
        var digital = new List<FileInfo>();
        string hdd = Path.Combine(gameDir, "dev_hdd0", "game");
        if (Directory.Exists(hdd))
        {
            foreach (string d in Directory.GetDirectories(hdd))
            {
                var eboot = new FileInfo(Path.Combine(d, "USRDIR", "EBOOT.BIN"));
                if (eboot.Exists && ReadParamSfo(Path.Combine(d, "PARAM.SFO")).GetValueOrDefault("CATEGORY") == "HG")
                    digital.Add(eboot);
            }
        }
        if (digital.Count == 1) return new GameFile(Relative(gameDir, digital[0]), "digital game installed in RPCS3");
        if (digital.Count > 1) throw new InvalidDataException($"several digital games in dev_hdd0\\game ({digital.Count}): keep one per folder");
        throw new InvalidDataException("no PS3 game found (PS3_GAME folder or .iso in games, or a digital game installed in RPCS3)");
    }

    /// <summary>
    /// I valori di un PARAM.SFO (il file con le informazioni di un gioco PS3). Formato binario:
    /// un'intestazione, poi una tabella di voci che puntano ai nomi e ai valori.
    /// </summary>
    internal static Dictionary<string, string> ReadParamSfo(string path)
    {
        var values = new Dictionary<string, string>();
        if (!File.Exists(path)) return values;
        byte[] b = File.ReadAllBytes(path);
        if (b.Length < 20 || BitConverter.ToUInt32(b, 0) != 0x46535000) return values;   // "\0PSF"
        int keyTable = (int)BitConverter.ToUInt32(b, 8);
        int dataTable = (int)BitConverter.ToUInt32(b, 12);
        int count = (int)BitConverter.ToUInt32(b, 16);
        for (int i = 0; i < count; i++)
        {
            int e = 20 + 16 * i;
            if (e + 16 > b.Length) break;
            int keyStart = keyTable + BitConverter.ToUInt16(b, e);
            int format = BitConverter.ToUInt16(b, e + 2);
            int length = (int)BitConverter.ToUInt32(b, e + 4);
            int dataStart = dataTable + (int)BitConverter.ToUInt32(b, e + 12);
            int keyEnd = Array.IndexOf(b, (byte)0, keyStart);
            if (keyEnd < 0 || dataStart + length > b.Length) break;
            string key = Encoding.ASCII.GetString(b, keyStart, keyEnd - keyStart);
            values[key] = format == 0x0404
                ? BitConverter.ToUInt32(b, dataStart).ToString()
                : Encoding.UTF8.GetString(b, dataStart, length).TrimEnd('\0');
        }
        return values;
    }
}
