// =====================================================================================
//  StoreCatalog.cs
//  L'elenco degli store che SteamAll conosce su questo sistema, e il riconoscimento dello store
//  di una voce già presente in Steam (serve per togliere le voci degli store spenti o non più
//  installati, come fa BoilR con i giochi che non trova più).
//
//    Windows:   GOG GALAXY, Epic Games, Ubisoft Connect
//    Linux, macOS: per ora nessuno (le cartelle dei giochi e gli emulatori funzionano lo stesso)
//  Ci sono solo gli store provati davvero. Per aggiungerne uno basta una classe che implementa
//  IStoreProvider (vedi i tre qui sotto) e una riga in For: il progetto è open source apposta.
//
//  Ogni store legge la SUA libreria e apre il SUO programma: niente passa da GOG GALAXY (che può
//  collegare anche Ubisoft, EA...), così "Installa" in Steam apre subito lo store giusto.
// =====================================================================================

using System.Text.RegularExpressions;
using SteamAll.Core.Platform;
using SteamAll.Core.Steam;

namespace SteamAll.Core.Stores;

public static class StoreCatalog
{
    /// <summary>I fornitori degli store per questa piattaforma, nell'ordine in cui si mostrano.</summary>
    public static List<IStoreProvider> For(IPlatform platform)
    {
        // Il secondo controllo (OperatingSystem.IsWindows) serve al compilatore: così sa che le
        // classi "solo Windows" qui sotto si usano davvero solo su Windows.
        if (platform.Os == OsKind.Windows && OperatingSystem.IsWindows())
            return [new GogGalaxyStore(platform), new EpicStore(platform), new UbisoftStore(platform)];
        return [];
    }

    /// <summary>Il fornitore con questo id ("epic"...), o null.</summary>
    public static IStoreProvider? Find(IPlatform platform, string id) =>
        For(platform).FirstOrDefault(s => s.Info.Id == id);

    /// <summary>
    /// Il prefisso di piattaforma di GOG GALAXY ("gog", "uplay"...) -> lo store di SteamAll. Serve
    /// solo a riconoscere le voci create in passato tramite GALAXY (Setup_BoilR, BoilR).
    /// </summary>
    public static string StoreIdForGalaxyPlatform(string prefix) => prefix switch
    {
        "uplay" => "ubisoft",
        "origin" or "eaapp" => "ea",
        "xboxone" => "xbox",
        _ => prefix,   // "gog", "amazon", "battlenet", "epic", "itch"...
    };

    // I "tag" che BoilR metteva alle sue voci -> lo store.
    private static readonly Dictionary<string, string> BoilRTags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Gog"] = "gog", ["Epic"] = "epic", ["Legendary"] = "epic", ["Heroic"] = "epic", ["Uplay"] = "ubisoft",
        ["Origin"] = "ea", ["Amazon"] = "amazon", ["Itch"] = "itch", ["Lutris"] = "lutris", ["GamePass"] = "xbox",
    };

    // L'inizio del link nelle opzioni (o l'exe) -> lo store.
    private static readonly (Regex Pattern, string Store)[] Links =
    [
        (new(@"com\.epicgames\.launcher://", RegexOptions.IgnoreCase), "epic"),
        (new(@"uplay://", RegexOptions.IgnoreCase), "ubisoft"),
        (new(@"(origin2?|link2ea)://", RegexOptions.IgnoreCase), "ea"),
        (new(@"amazon-games://", RegexOptions.IgnoreCase), "amazon"),
        (new(@"itch://", RegexOptions.IgnoreCase), "itch"),
        (new(@"lutris:", RegexOptions.IgnoreCase), "lutris"),
        (new(@"shell:AppsFolder\\", RegexOptions.IgnoreCase), "xbox"),
        (new(@"heroic://launch/(legendary|epic)/", RegexOptions.IgnoreCase), "epic"),
        (new(@"heroic://launch/gog/", RegexOptions.IgnoreCase), "gog"),
        (new(@"heroic://launch/(nile|amazon)/", RegexOptions.IgnoreCase), "amazon"),
    ];

    /// <summary>
    /// Lo store di una voce di Steam: "gog", "epic"..., "folder" o "emulator" per le nostre voci di
    /// cartelle ed emulatori, oppure null se non si capisce.
    /// </summary>
    public static string? Identify(Shortcut s)
    {
        // 1. Le nostre voci lo scrivono nel marchio: "steamall:epic", "steamall-store:gog"...
        if (Markers.StoreOf(s.DevkitGameId) is { } fromMarker) return fromMarker;

        // 2. I link di GOG GALAXY dicono la piattaforma nel "releaseKey": gog_123, uplay_..., origin_...
        Match galaxy = Regex.Match(s.LaunchOptions, @"goggalaxy://(?:openGameView|launchGame)/([a-z]+)_", RegexOptions.IgnoreCase);
        if (galaxy.Success) return StoreIdForGalaxyPlatform(galaxy.Groups[1].Value.ToLowerInvariant());

        // 3. Gli altri link degli store.
        foreach ((Regex pattern, string store) in Links)
            if (pattern.IsMatch(s.LaunchOptions) || pattern.IsMatch(s.Exe)) return store;

        // 4. Le voci di BoilR: i suoi tag ("Standalone" = una cartella dei giochi).
        foreach (string tag in s.Tags)
        {
            if (tag.Equals("Standalone", StringComparison.OrdinalIgnoreCase)) return Markers.FolderStore;
            if (BoilRTags.TryGetValue(tag, out string? store)) return store;
        }
        return null;
    }
}
