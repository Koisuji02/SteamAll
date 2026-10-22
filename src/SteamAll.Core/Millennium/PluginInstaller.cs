// =====================================================================================
//  PluginInstaller.cs
//  Il plugin Millennium del tasto Installa: controlla se c'è, lo installa, lo toglie.
//  Millennium (https://steambrew.app) è un programma che aggiunge temi e plugin a Steam. I
//  plugin stanno in Steam\millennium\plugins\<nome>\ e quelli attivi sono elencati in
//  Steam\millennium\config\config.json ("plugins" > "enabledPlugins").
//  Installare = copiare i file del plugin (che SteamAll porta dentro di sé) e attivarlo.
//  Modifica la configurazione di Millennium: lo fa solo quando premi il pulsante, dopo una conferma,
//  e lascia una copia di sicurezza di config.json.
// =====================================================================================

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using SteamAll.Core.Platform;

namespace SteamAll.Core.Millennium;

public enum PluginState { MillenniumMissing, NotInstalled, Disabled, Outdated, Installed }

public static class PluginInstaller
{
    public const string PluginName = "steamall-install-button";
    private const string ResourcePrefix = "MillenniumPlugin/";

    private static string MillenniumFolder(string steamFolder) => Path.Combine(steamFolder, "millennium");
    private static string PluginFolder(string steamFolder) => Path.Combine(MillenniumFolder(steamFolder), "plugins", PluginName);
    private static string ConfigFile(string steamFolder) => Path.Combine(MillenniumFolder(steamFolder), "config", "config.json");

    /// <summary>A che punto è il plugin in questo Steam.</summary>
    public static PluginState Status(string steamFolder)
    {
        if (!Directory.Exists(MillenniumFolder(steamFolder))) return PluginState.MillenniumMissing;
        if (!Directory.Exists(PluginFolder(steamFolder))) return PluginState.NotInstalled;
        bool sameFiles = BundledFiles().All(f =>
        {
            string target = Path.Combine(PluginFolder(steamFolder), f.Key);
            return File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(f.Value);
        });
        if (!sameFiles) return PluginState.Outdated;
        return EnabledPlugins(steamFolder)?.Contains(PluginName) == true ? PluginState.Installed : PluginState.Disabled;
    }

    /// <summary>Copia i file del plugin e lo attiva in Millennium. Poi va riavviato Steam.</summary>
    public static void Install(string steamFolder)
    {
        if (!Directory.Exists(MillenniumFolder(steamFolder)))
            throw new InvalidOperationException("Millennium is not installed in Steam (https://steambrew.app)");
        foreach (KeyValuePair<string, byte[]> file in BundledFiles())
        {
            string target = Path.Combine(PluginFolder(steamFolder), file.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, file.Value);   // sono file nostri, nella cartella del nostro plugin
        }
        SetEnabled(steamFolder, true);
    }

    /// <summary>Disattiva il plugin e sposta la sua cartella nel Cestino.</summary>
    public static void Uninstall(string steamFolder, IPlatform platform)
    {
        SetEnabled(steamFolder, false);
        if (Directory.Exists(PluginFolder(steamFolder))) platform.MoveToRecycleBin(PluginFolder(steamFolder));
    }

    // I file del plugin portati da SteamAll: percorso relativo -> contenuto.
    internal static Dictionary<string, byte[]> BundledFiles()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        Assembly assembly = Assembly.GetExecutingAssembly();
        foreach (string name in assembly.GetManifestResourceNames())
        {
            // Compilando su Windows i nomi delle risorse hanno "\", su Linux "/": li rendiamo uguali.
            string normalized = name.Replace('\\', '/');
            if (!normalized.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;
            using Stream stream = assembly.GetManifestResourceStream(name)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            files[normalized[ResourcePrefix.Length..]] = memory.ToArray();
        }
        return files;
    }

    private static List<string>? EnabledPlugins(string steamFolder)
    {
        string file = ConfigFile(steamFolder);
        if (!File.Exists(file)) return null;
        JsonNode? config = JsonHelpers.TryParse(File.ReadAllText(file));
        return config.Field("plugins").Items("enabledPlugins").Select(p => p.AsText()).ToList();
    }

    // Aggiunge o toglie il nostro plugin dall'elenco dei plugin attivi, toccando solo quello.
    private static void SetEnabled(string steamFolder, bool enabled)
    {
        string file = ConfigFile(steamFolder);
        JsonObject config = (File.Exists(file) ? JsonHelpers.TryParse(File.ReadAllText(file)) as JsonObject : null) ?? new JsonObject();
        if (config["plugins"] is not JsonObject plugins) config["plugins"] = plugins = new JsonObject();
        if (plugins["enabledPlugins"] is not JsonArray list) plugins["enabledPlugins"] = list = new JsonArray();

        bool present = list.Any(p => p.AsText() == PluginName);
        if (present == enabled) return;   // già com'è richiesto: non tocchiamo il file
        if (enabled) list.Add(PluginName);
        else
        {
            foreach (JsonNode? p in list.Where(p => p.AsText() == PluginName).ToList()) list.Remove(p);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (File.Exists(file)) File.Copy(file, file + ".steamall-backup", overwrite: true);
        File.WriteAllText(file, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
