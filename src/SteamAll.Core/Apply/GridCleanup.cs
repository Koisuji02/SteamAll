// =====================================================================================
//  GridCleanup.cs
//  La pulizia della cartella grid di Steam (le immagini personalizzate): nel Cestino vanno
//    - le immagini dei giochi NON Steam che non ci sono più (app id da 2^31 in su senza una voce);
//    - le immagini vuote (0 byte: download falliti).
//  Le immagini dei giochi Steam veri (app id più piccoli) non si toccano mai.
// =====================================================================================

using System.Text.RegularExpressions;
using SteamAll.Core.Platform;
using SteamAll.Core.Steam;

namespace SteamAll.Core.Apply;

public static class GridCleanup
{
    // <appid><tipo>.<estensione>: "4113688513p.png", "4113688513_hero.jpg", "4113688513.json"...
    private static readonly Regex ImageName =
        new(@"^(\d+)(p|_hero|_logo|_icon|-icon|_bigpicture)?\.(png|jpe?g|webp|ico|json)$", RegexOptions.IgnoreCase);

    /// <summary>I file che la pulizia toglierebbe (per l'anteprima e per Clean).</summary>
    public static List<FileInfo> Candidates(ShortcutLibrary library)
    {
        if (!Directory.Exists(library.GridFolder)) return [];
        HashSet<uint> alive = library.Shortcuts.Select(s => s.AppId).ToHashSet();
        var result = new List<FileInfo>();
        foreach (FileInfo file in new DirectoryInfo(library.GridFolder).EnumerateFiles())
        {
            Match m = ImageName.Match(file.Name);
            if (!m.Success || !ulong.TryParse(m.Groups[1].Value, out ulong id)) continue;
            bool empty = file.Length == 0 && !m.Groups[3].Value.Equals("json", StringComparison.OrdinalIgnoreCase);
            bool orphan = id >= 0x8000_0000 && id <= uint.MaxValue && !alive.Contains((uint)id);
            if (empty || orphan) result.Add(file);
        }
        return result;
    }

    /// <summary>Sposta i file nel Cestino; restituisce quanti. "warn" riceve gli errori (un file in uso...).</summary>
    public static int Clean(ShortcutLibrary library, IPlatform platform, Action<string> warn)
    {
        int count = 0;
        foreach (FileInfo file in Candidates(library))
        {
            try
            {
                platform.MoveToRecycleBin(file.FullName);
                count++;
            }
            catch (IOException ex)
            {
                warn($"Cleanup: {file.Name}: {ex.Message}");
            }
        }
        return count;
    }
}
