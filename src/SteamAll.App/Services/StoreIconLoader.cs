// =====================================================================================
//  StoreIconLoader.cs
//  Le icone delle schede (store e Millennium):
//    1. store installato, su Windows: l'icona del suo programma (ExeIcon), grande e nitida;
//    2. altrimenti l'icona del sito, scaricata una volta e tenuta in
//       %LOCALAPPDATA%\SteamAll\cache\icons (solo una copia: si può cancellare);
//    3. Millennium ha solo un'icona SVG (disegno vettoriale): ne prendiamo le forme ("path") e le
//       disegniamo noi, così resta nitida a qualunque dimensione.
//  Se niente funziona, la scheda mostra le iniziali.
// =====================================================================================

using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SteamAll.Core.Platform;

namespace SteamAll.Services;

public static class StoreIconLoader
{
    // Alcuni siti rifiutano le richieste senza "User-Agent" (il nome del programma che chiede): lo diamo.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15), DefaultRequestHeaders = { { "User-Agent", "SteamAll/0.1" } } };

    private static string CacheFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamAll", "cache", "icons");

    /// <summary>L'icona di uno store: quella del programma se è installato (Windows), altrimenti quella del sito.</summary>
    public static async Task<Bitmap?> LoadAsync(string id, string url, string protocol, IPlatform platform)
    {
        if (OperatingSystem.IsWindows() && protocol.Length > 0 && platform.ProtocolExe(protocol) is { } exe)
        {
            // Il controllo si ripete dentro la lambda: il compilatore guarda ogni metodo per conto suo.
            Bitmap? fromExe = await Task.Run(() => OperatingSystem.IsWindows() ? ExeIcon.Load(exe) : null);
            if (fromExe is not null) return fromExe;
        }
        return await LoadAsync(id, url);
    }

    /// <summary>L'icona all'indirizzo "url" (cache per "id"), o null.</summary>
    public static async Task<Bitmap?> LoadAsync(string id, string url)
    {
        try
        {
            string file = await DownloadAsync(id + Path.GetExtension(new Uri(url).AbsolutePath), url);
            // Avalonia legge PNG, JPEG e anche le icone .ico (prende la prima immagine).
            return await Task.Run(() =>
            {
                using FileStream stream = File.OpenRead(file);
                return Bitmap.DecodeToWidth(stream, 128, BitmapInterpolationMode.HighQuality);
            });
        }
        catch (Exception)   // rete assente, formato che non si legge: le iniziali
        {
            return null;
        }
    }

    /// <summary>
    /// Le forme di un'icona SVG, unite in una sola geometria da disegnare con un Path. Le icone SVG
    /// scritte "alla vecchia" hanno l'asse verticale capovolto: lo dice "flipped".
    /// </summary>
    public static async Task<Geometry?> LoadSvgAsync(string id, string url)
    {
        try
        {
            string svg = await File.ReadAllTextAsync(await DownloadAsync(id + ".svg", url));
            var group = new GeometryGroup();
            // Ogni <path d="..."> è una forma; la sintassi di "d" è la stessa delle geometrie di Avalonia.
            foreach (Match m in Regex.Matches(svg, "<path[^>]*\\sd=\"([^\"]+)\"")) group.Children.Add(Geometry.Parse(m.Groups[1].Value));
            return group.Children.Count > 0 ? group : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Scarica un file nella cache (se non c'è già) e restituisce il percorso.
    private static async Task<string> DownloadAsync(string name, string url)
    {
        string file = Path.Combine(CacheFolder, name);
        if (File.Exists(file)) return file;
        byte[] data = await Http.GetByteArrayAsync(url);
        Directory.CreateDirectory(CacheFolder);
        await File.WriteAllBytesAsync(file, data);
        return file;
    }
}
