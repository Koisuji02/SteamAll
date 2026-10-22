// =====================================================================================
//  SteamGridDbClient.cs
//  Le immagini da SteamGridDB (https://www.steamgriddb.com), il sito da cui le prende anche
//  BoilR: copertine, banner, sfondi (hero), loghi e icone. Serve la tua chiave API, che va
//  nell'intestazione "Authorization" di ogni richiesta e non viene mai scritta da nessuna parte.
// =====================================================================================

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SteamAll.Core.Artwork;

/// <summary>Un'immagine trovata su SteamGridDB.</summary>
/// <param name="Thumb">La miniatura (più leggera), per mostrare le alternative; "" se SteamGridDB non la dà.</param>
public sealed record GridImage(string Url, int Width, int Height, string Language, string Thumb = "");

public sealed class SteamGridDbClient
{
    private const string Api = "https://www.steamgriddb.com/api/v2/";
    private readonly HttpClient http;
    private readonly string key;

    // Il costruttore riceve l'HttpClient da fuori: il programma usa quello vero, le prove uno finto
    // che risponde da sé (così le prove non vanno su Internet).
    public SteamGridDbClient(HttpClient http, string apiKey)
    {
        this.http = http;
        key = apiKey;
    }

    public bool HasKey => key.Length > 0;

    /// <summary>
    /// L'id SteamGridDB del gioco: come BoilR, il primo risultato della ricerca per nome. Se non
    /// trova niente riprova senza ™/®, senza l'edizione ("Game of the Year"...) e senza sottotitolo.
    /// </summary>
    public async Task<(long Id, string Name)?> FindGameAsync(string title)
    {
        foreach (string attempt in SearchAttempts(title))
        {
            JsonNode? json = await GetJsonAsync("search/autocomplete/" + Uri.EscapeDataString(attempt));
            JsonNode? first = json.Items("data").FirstOrDefault();
            if (first is not null && long.TryParse(first.Text("id"), out long id)) return (id, first.Text("name"));
        }
        return null;   // un "(long, string)?" può valere null: la tupla intera manca
    }

    /// <summary>Le varianti del titolo da provare, senza doppioni.</summary>
    internal static IEnumerable<string> SearchAttempts(string title)
    {
        // Via i simboli di marchio (™ ® ©), e i trattini lunghi (– —) diventano normali (-).
        string clean = Regex.Replace(Regex.Replace(title, "[™®©]", ""), "[–—]", "-");
        clean = Regex.Replace(clean, @"\s+", " ").Trim();
        var attempts = new List<string>
        {
            clean,
            Regex.Replace(clean, @"(?i)[\s:-]*\b(game of the year|goty|definitive|deluxe|ultimate|complete|gold|enhanced|remastered)\b.*$", "").Trim(),
        };
        foreach (string separator in (string[])[" - ", ": "])
        {
            int i = clean.IndexOf(separator, StringComparison.Ordinal);
            if (i > 2) attempts.Add(clean[..i].Trim());
        }
        return attempts.Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// L'immagine migliore di un tipo, o null. SteamGridDB le dà già in ordine di gradimento; noi
    /// preferiamo quelle in inglese, in italiano o senza testo. Per gli hero, con hd = true, solo
    /// 3840x1240 (se non ci sono, null).
    /// </summary>
    public async Task<GridImage?> BestImageAsync(long gameId, ArtworkKind kind, bool hd = false)
    {
        List<GridImage> all = await ImagesAsync(gameId, kind, hd);
        return all.FirstOrDefault(i => i.Language is "en" or "it" or "") ?? all.FirstOrDefault();
    }

    /// <summary>
    /// Tutte le immagini di un tipo per un gioco, nell'ordine di gradimento di SteamGridDB, con la
    /// loro miniatura (Thumb): servono anche alla finestra in cui scegli un'alternativa.
    /// </summary>
    public async Task<List<GridImage>> ImagesAsync(long gameId, ArtworkKind kind, bool hd = false)
    {
        string query = kind switch
        {
            ArtworkKind.Cover => $"grids/game/{gameId}?dimensions=600x900&types=static&nsfw=false&humor=false&mimes=image/png,image/jpeg",
            ArtworkKind.Banner => $"grids/game/{gameId}?dimensions=920x430,460x215&types=static&nsfw=false&humor=false&mimes=image/png,image/jpeg",
            ArtworkKind.Hero when hd => $"heroes/game/{gameId}?dimensions=3840x1240&types=static&nsfw=false&humor=false&mimes=image/png,image/jpeg",
            ArtworkKind.Hero => $"heroes/game/{gameId}?types=static&nsfw=false&humor=false&mimes=image/png,image/jpeg",
            ArtworkKind.Logo => $"logos/game/{gameId}?types=static&nsfw=false&humor=false&mimes=image/png",
            _ => $"icons/game/{gameId}?types=static&nsfw=false&humor=false&mimes=image/png,image/vnd.microsoft.icon",
        };
        JsonNode? json = await GetJsonAsync(query);
        return json.Items("data")
            .Select(d => new GridImage(d.Text("url"), int.TryParse(d.Text("width"), out int w) ? w : 0,
                                       int.TryParse(d.Text("height"), out int h) ? h : 0, d.Text("language"), d.Text("thumb")))
            .Where(i => i.Url.Length > 0 && (!(kind == ArtworkKind.Hero && hd) || i.Width >= 3840))
            .ToList();
    }

    /// <summary>Scarica i byte di un'immagine (da SteamGridDB o da uno store).</summary>
    public async Task<byte[]> DownloadAsync(string url) => await http.GetByteArrayAsync(url);

    // Una richiesta all'API: null se la risorsa non esiste (404).
    private async Task<JsonNode?> GetJsonAsync(string relative)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Api + relative);
        // "Bearer <chiave>": il modo standard di passare una chiave API. La chiave resta in memoria.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using HttpResponseMessage response = await http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("SteamGridDB doesn't accept the API key: check it in Settings");
        response.EnsureSuccessStatusCode();   // per qualunque altro errore lancia un'eccezione
        return JsonHelpers.TryParse(await response.Content.ReadAsStringAsync());
    }
}
