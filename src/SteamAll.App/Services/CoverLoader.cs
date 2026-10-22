// =====================================================================================
//  CoverLoader.cs
//  Le copertine della griglia (2:3, come quelle di Steam), da due posti:
//    1. la cartella grid di Steam, se il gioco ha già le sue immagini;
//    2. altrimenti l'immagine dello store (GOG, Epic), scaricata una volta e tenuta in una cache:
//       %LOCALAPPDATA%\SteamAll\cache\covers  (è solo una copia: si può cancellare quando vuoi).
//  Le immagini vengono rimpicciolite mentre si leggono, così la memoria resta poca.
// =====================================================================================

using System.Security.Cryptography;   // SHA256: un "riassunto" dell'indirizzo, per il nome del file in cache
using System.Text;
using Avalonia.Media.Imaging;          // Bitmap: un'immagine che Avalonia sa disegnare
using SteamAll.Core.Plan;

namespace SteamAll.Services;

public static class CoverLoader
{
    // Un solo HttpClient per tutto il programma (è fatto per essere riusato).
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    // Al massimo 4 download insieme, per non intasare la rete. SemaphoreSlim è un "contatore di
    // posti": WaitAsync prende un posto (o aspetta che se ne liberi uno), Release lo restituisce.
    private static readonly SemaphoreSlim Downloads = new(4);

    private static string CacheFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamAll", "cache", "covers");

    /// <summary>
    /// La copertina di una riga del piano, larga "width" pixel; null se non ce n'è nessuna o
    /// non si riesce a leggerla (la griglia mostrerà le iniziali del gioco).
    /// "async Task&lt;Bitmap?&gt;" = un metodo asincrono: chi lo chiama scrive "await" e intanto
    /// l'interfaccia non si blocca.
    /// </summary>
    public static async Task<Bitmap?> LoadAsync(PlanEntry entry, int width)
    {
        try
        {
            string? file = entry.CoverFile;
            if (file is null && entry.Game.StoreImages.TryGetValue("p", out string? url))
                file = await DownloadAsync(url);
            if (file is null) return null;

            // Decodificare un'immagine è lavoro "pesante": lo facciamo su un altro thread (Task.Run).
            return await Task.Run(() =>
            {
                using FileStream stream = File.OpenRead(file);
                return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.MediumQuality);
            });
        }
        catch (Exception)   // rete assente, file rovinato, formato sconosciuto: niente copertina
        {
            return null;
        }
    }

    /// <summary>Un'immagine da un indirizzo (passando dalla cache), larga "width" pixel; null se non si legge.</summary>
    public static async Task<Bitmap?> LoadUrlAsync(string url, int width)
    {
        try
        {
            string? file = await DownloadAsync(url);
            return file is null ? null : await LoadFileAsync(file, width);
        }
        catch (Exception) { return null; }
    }

    /// <summary>Un'immagine da un file sul disco (per esempio nella cartella grid di Steam), o null.</summary>
    public static async Task<Bitmap?> LoadFileAsync(string file, int width)
    {
        try
        {
            return await Task.Run(() =>
            {
                using FileStream stream = File.OpenRead(file);
                return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.MediumQuality);
            });
        }
        catch (Exception) { return null; }
    }

    // Scarica un'immagine nella cache (se non c'è già) e restituisce il file.
    private static async Task<string?> DownloadAsync(string url)
    {
        // Il nome del file è il riassunto SHA-256 dell'indirizzo: sempre uguale per lo stesso
        // indirizzo, e senza caratteri vietati nei nomi dei file. Convert.ToHexString = in esadecimale.
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        string file = Path.Combine(CacheFolder, name + ".img");
        if (File.Exists(file)) return file;

        await Downloads.WaitAsync();
        try
        {
            if (File.Exists(file)) return file;   // nel frattempo l'ha scaricata un altro
            byte[] data = await Http.GetByteArrayAsync(url);
            Directory.CreateDirectory(CacheFolder);
            await File.WriteAllBytesAsync(file, data);
            return file;
        }
        finally
        {
            Downloads.Release();
        }
    }
}
