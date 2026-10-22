// =====================================================================================
//  ThemeInstaller.cs
//  Il tema consigliato, "Steam 2.0 Modular (Windows Accent)": una modifica di Steam 2.0 Modular di
//  Von-Amadeus (https://github.com/Br1Rol2/Steam2.0-modular), a cui va il merito del tema originale.
//  SteamAll lo porta dentro di sé (risorse "MillenniumTheme/...", dalla cartella
//  src/SteamAll.MillenniumTheme) e lo installa con un pulsante, dopo una conferma:
//    - copia i file in Steam\millennium\themes\Steam2.0-modular-WindowsAccent (la versione che c'era
//      prima va nel Cestino);
//    - lo rende il tema attivo in Steam\millennium\config\config.json ("themes" > "activeTheme"),
//      lasciando una copia di sicurezza di config.json.
// =====================================================================================

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using SteamAll.Core.Platform;

namespace SteamAll.Core.Millennium;

public enum ThemeState { MillenniumMissing, NotInstalled, Outdated, Installed }

public static class ThemeInstaller
{
    public const string ThemeFolder = "Steam2.0-modular-WindowsAccent";
    private const string ResourcePrefix = "MillenniumTheme/" + ThemeFolder + "/";

    private static string Folder(string steam) => Path.Combine(steam, "millennium", "themes", ThemeFolder);
    private static string ConfigFile(string steam) => Path.Combine(steam, "millennium", "config", "config.json");

    /// <summary>A che punto è il tema in questo Steam: installato uguale a quello di SteamAll, diverso, o assente.</summary>
    public static ThemeState Status(string steam)
    {
        if (!Directory.Exists(Path.Combine(steam, "millennium"))) return ThemeState.MillenniumMissing;
        if (!Directory.Exists(Folder(steam))) return ThemeState.NotInstalled;
        bool same = BundledFiles().All(f =>
        {
            string target = Path.Combine(Folder(steam), f.Key);
            return File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(f.Value);
        });
        return same ? ThemeState.Installed : ThemeState.Outdated;
    }

    /// <summary>Il tema è quello attivo in Millennium?</summary>
    public static bool IsActive(string steam) =>
        File.Exists(ConfigFile(steam)) &&
        JsonHelpers.TryParse(File.ReadAllText(ConfigFile(steam))).Field("themes").Text("activeTheme") == ThemeFolder;

    /// <summary>Installa (o aggiorna) il tema e lo rende attivo. Poi Steam va riavviato.</summary>
    public static void Install(string steam, IPlatform platform)
    {
        if (!Directory.Exists(Path.Combine(steam, "millennium")))
            throw new InvalidOperationException("Millennium is not installed in Steam (https://steambrew.app)");
        if (Directory.Exists(Folder(steam))) platform.MoveToRecycleBin(Folder(steam));   // la versione di prima, recuperabile
        foreach ((string relative, byte[] data) in BundledFiles())
        {
            string target = Path.Combine(Folder(steam), relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, data);
        }
        SetActive(steam);
    }

    /// <summary>I file del tema portati da SteamAll: percorso dentro la cartella del tema -> contenuto.</summary>
    internal static Dictionary<string, byte[]> BundledFiles()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        Assembly assembly = Assembly.GetExecutingAssembly();
        foreach (string name in assembly.GetManifestResourceNames())
        {
            string normalized = name.Replace('\\', '/');   // "\" compilando su Windows, "/" su Linux
            if (!normalized.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;
            using Stream stream = assembly.GetManifestResourceStream(name)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            files[normalized[ResourcePrefix.Length..]] = memory.ToArray();
        }
        return files;
    }

    // "themes" > "activeTheme" = la cartella del nostro tema; il resto della configurazione resta com'è.
    private static void SetActive(string steam)
    {
        string file = ConfigFile(steam);
        JsonObject config = (File.Exists(file) ? JsonHelpers.TryParse(File.ReadAllText(file)) as JsonObject : null) ?? new JsonObject();
        if (config["themes"] is not JsonObject themes) config["themes"] = themes = new JsonObject();
        if (themes["activeTheme"].AsText() == ThemeFolder) return;
        themes["activeTheme"] = ThemeFolder;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (File.Exists(file)) File.Copy(file, file + ".steamall-backup", overwrite: true);
        File.WriteAllText(file, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
