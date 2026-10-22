// =====================================================================================
//  ArtworkService.cs
//  Mette nella cartella grid di Steam le immagini che mancano a un gioco: prima da SteamGridDB,
//  e per quello che lì non c'è dalle immagini dello store (GOG, Epic). Con "hero HD" sostituisce
//  anche gli hero piccoli con quelli 3840x1240, ma non tocca quelli che hai scelto tu.
//
//  Come capiamo che un hero l'hai scelto tu: quando ne scriviamo uno, ricordiamo il suo
//  "riassunto" (hash SHA-256) in artwork.json. Se più avanti il file nella cartella ha un hash
//  diverso, vuol dire che l'hai cambiato a mano (per esempio da Steam): da lì in poi resta tuo.
// =====================================================================================

using System.Security.Cryptography;
using System.Text.Json;
using SteamAll.Core.Platform;

namespace SteamAll.Core.Artwork;

/// <summary>Il lavoro sulle immagini di un gioco.</summary>
/// <param name="StoreImages">Le immagini dello store per tipo (ripiego), vedi Game.StoreImages.</param>
/// <param name="LegacyHeroHash">L'hash dell'hero scritto da Setup_BoilR (dal suo stato.ini), se c'è.</param>
/// <param name="Choices">Le immagini che hai scelto tu (tipo -> indirizzo su SteamGridDB): hanno sempre la precedenza.</param>
public sealed record ArtworkJob(uint AppId, string Title, IReadOnlyDictionary<string, string> StoreImages, string? LegacyHeroHash = null,
                                IReadOnlyDictionary<ArtworkKind, string>? Choices = null);

/// <summary>Com'è andata: quante immagini scritte, e il file dell'icona (per il campo "icon" della voce).</summary>
public sealed record ArtworkResult(int Written, int Missing, string? IconFile, string Message);

public sealed class ArtworkService(SteamGridDbClient client, IPlatform platform, string gridFolder, string stateFile, bool heroHd)
{
    // Gli hash degli hero scritti da noi: app id -> SHA-256. "Lazy" = si legge dal disco solo la prima volta che serve.
    private readonly Lazy<Dictionary<string, string>> heroHashes = new(() => LoadState(stateFile));

    public async Task<ArtworkResult> EnsureAsync(ArtworkJob job)
    {
        // Le immagini che hai scelto e che non sono ancora in Steam: nel file di stato ricordiamo
        // l'indirizzo dell'ultima scritta ("choice:<app id>:<tipo>"), così non si riscaricano ogni volta.
        IReadOnlyDictionary<ArtworkKind, string> choices = job.Choices ?? new Dictionary<ArtworkKind, string>();
        List<ArtworkKind> chosen = choices.Where(c => heroHashes.Value.GetValueOrDefault(ChoiceKey(job.AppId, c.Key)) != c.Value)
                                          .Select(c => c.Key).ToList();

        // Quali immagini mancano? E l'hero va migliorato (solo se non l'hai scelto tu)?
        List<ArtworkKind> missing = Enum.GetValues<ArtworkKind>()
            .Where(k => !choices.ContainsKey(k) && ArtworkFiles.Find(gridFolder, job.AppId, k) is null)
            .ToList();
        bool upgradeHero = heroHd && !choices.ContainsKey(ArtworkKind.Hero) && !missing.Contains(ArtworkKind.Hero) && HeroNeedsUpgrade(job);

        int written = 0, stillMissing = 0;
        foreach (ArtworkKind kind in chosen)
        {
            byte[]? data = await TryAsync(() => client.DownloadAsync(choices[kind]));
            if (data is null || ArtworkFiles.Extension(data) is null) { stillMissing++; continue; }
            // Quella di prima va nel Cestino (mai cancellata), poi scriviamo quella scelta.
            string? old = ArtworkFiles.Find(gridFolder, job.AppId, kind);
            if (old is not null) platform.MoveToRecycleBin(old);
            WriteImage(job.AppId, kind, data);
            heroHashes.Value[ChoiceKey(job.AppId, kind)] = choices[kind];
            SaveState();
            written++;
        }

        if (missing.Count == 0 && !upgradeHero)
            return new ArtworkResult(written, stillMissing, ArtworkFiles.Find(gridFolder, job.AppId, ArtworkKind.Icon),
                                     stillMissing > 0 ? "a chosen image couldn't be downloaded" : "");

        (long Id, string Name)? game = null;
        string note = "";
        if (client.HasKey)
        {
            try
            {
                game = await client.FindGameAsync(job.Title);
                if (game is null) note = "SteamGridDB doesn't know this title";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                note = "SteamGridDB can't be reached";
            }
        }

        foreach (ArtworkKind kind in missing)
        {
            byte[]? data = await GetImageAsync(job, game?.Id, kind);
            if (data is null) { stillMissing++; continue; }
            WriteImage(job.AppId, kind, data);
            written++;
        }

        if (upgradeHero && game is not null)
        {
            GridImage? hd = await TryAsync(() => client.BestImageAsync(game.Value.Id, ArtworkKind.Hero, hd: true));
            byte[]? data = hd is null ? null : await TryAsync(() => client.DownloadAsync(hd.Url));
            if (data is not null && ArtworkFiles.Size(data) is { Width: >= 3840 })
            {
                // Il vecchio hero va nel Cestino (mai cancellato), poi scriviamo quello nuovo.
                string? old = ArtworkFiles.Find(gridFolder, job.AppId, ArtworkKind.Hero);
                if (old is not null) platform.MoveToRecycleBin(old);
                WriteImage(job.AppId, ArtworkKind.Hero, data);
                written++;
            }
        }

        if (game is not null && !string.Equals(Games.Names.Normalize(game.Value.Name), Games.Names.Normalize(job.Title), StringComparison.Ordinal))
            note = AddNote(note, $"artwork of \"{game.Value.Name}\"");
        if (stillMissing > 0) note = AddNote(note, $"{stillMissing} image(s) not found");
        return new ArtworkResult(written, stillMissing, ArtworkFiles.Find(gridFolder, job.AppId, ArtworkKind.Icon), note);
    }

    private static string AddNote(string note, string more) => note.Length > 0 ? $"{note}; {more}" : more;

    private static string ChoiceKey(uint appId, ArtworkKind kind) => $"choice:{appId}:{kind}";

    // Un'immagine: da SteamGridDB (hero HD se richiesto, poi qualunque), altrimenti dallo store.
    private async Task<byte[]?> GetImageAsync(ArtworkJob job, long? gameId, ArtworkKind kind)
    {
        if (gameId is long id)   // "is long id" = se ha un valore, chiamalo id
        {
            GridImage? image = null;
            if (kind == ArtworkKind.Hero && heroHd) image = await TryAsync(() => client.BestImageAsync(id, kind, hd: true));
            image ??= await TryAsync(() => client.BestImageAsync(id, kind));
            byte[]? data = image is null ? null : await TryAsync(() => client.DownloadAsync(image.Url));
            if (data is not null && ArtworkFiles.Extension(data) is not null) return data;
        }
        if (job.StoreImages.TryGetValue(ArtworkFiles.StoreKey(kind), out string? url))
        {
            byte[]? data = await TryAsync(() => client.DownloadAsync(url));
            if (data is not null && ArtworkFiles.Extension(data) is not null) return data;
        }
        return null;
    }

    // Esegue una richiesta e, se va male (rete, server), dà null invece di un errore: un'immagine
    // mancante non deve fermare tutto. "Func<Task<T>>" = un metodo asincrono passato come valore;
    // "T?" = se va male restituisce "default" (per le classi è null).
    private static async Task<T?> TryAsync<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException) { return default; }
    }

    private void WriteImage(uint appId, ArtworkKind kind, byte[] data)
    {
        Directory.CreateDirectory(gridFolder);
        string file = Path.Combine(gridFolder, $"{ArtworkFiles.BaseName(appId, kind)}.{ArtworkFiles.Extension(data)}");
        File.WriteAllBytes(file, data);
        if (kind == ArtworkKind.Hero)
        {
            heroHashes.Value[appId.ToString()] = Hash(data);
            SaveState();
        }
    }

    // L'hero esistente è piccolo e non l'hai cambiato tu?
    private bool HeroNeedsUpgrade(ArtworkJob job)
    {
        string? file = ArtworkFiles.Find(gridFolder, job.AppId, ArtworkKind.Hero);
        if (file is null) return false;
        byte[] data = File.ReadAllBytes(file);
        if (ArtworkFiles.Size(data) is { Width: >= 3840 }) return false;   // già HD
        string current = Hash(data);
        // C'è un hash ricordato (nostro o di Setup_BoilR) e non coincide: l'hai scelto tu, resta.
        string? known = heroHashes.Value.GetValueOrDefault(job.AppId.ToString()) ?? job.LegacyHeroHash;
        return known is null || string.Equals(known, current, StringComparison.OrdinalIgnoreCase);
    }

    internal static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    private static Dictionary<string, string> LoadState(string file)
    {
        try
        {
            if (File.Exists(file)) return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }
        return [];
    }

    private void SaveState()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
        File.WriteAllText(stateFile, JsonSerializer.Serialize(heroHashes.Value));
    }
}
