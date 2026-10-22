// =====================================================================================
//  Markers.cs
//  I "marchi" che SteamAll scrive nel campo DevkitGameID delle sue voci di Steam. Dicono chi ha
//  creato la voce e di che tipo è, così la riconosciamo dopo: per aggiornarla, e per toglierla
//  quando il gioco non c'è più o il suo store è spento (come fa BoilR con le sue voci).
//
//      steamall:folder           un gioco di una cartella dei giochi
//      steamall:emulator         un gioco di un emulatore installato (cartelle di ROM)
//      steamall:<store>          un gioco installato di uno store ("steamall:epic", "steamall:ubisoft"...)
//      steamall-store:<store>    un gioco NON installato: il segnaposto con il tasto Installa
//
//  Le versioni precedenti scrivevano solo "steamall" e "steamall-store"; Setup_BoilR scriveva
//  "setupboilr-store" per i suoi segnaposto. Le riconosciamo ancora tutte (vedi Shortcut).
// =====================================================================================

namespace SteamAll.Core.Steam;

public static class Markers
{
    public const string SteamAll = "steamall";
    public const string Placeholder = "steamall-store";
    public const string SetupBoilRPlaceholder = "setupboilr-store";

    public const string FolderStore = "folder";
    public const string EmulatorStore = "emulator";

    /// <summary>Il marchio di un gioco installato di uno store, di una cartella o di un emulatore.</summary>
    public static string For(string storeId) => $"{SteamAll}:{storeId}";

    /// <summary>Il marchio di un segnaposto (gioco non installato) di uno store.</summary>
    public static string ForPlaceholder(string storeId) => $"{Placeholder}:{storeId}";

    public static string Folder => For(FolderStore);
    public static string Emulator => For(EmulatorStore);

    /// <summary>Lo store scritto nel marchio ("epic" da "steamall:epic"), o null se il marchio non lo dice.</summary>
    public static string? StoreOf(string marker)
    {
        // Prima il segnaposto: "steamall-store:..." inizia anche lui con "steamall".
        foreach (string prefix in (string[])[Placeholder + ":", SteamAll + ":"])
            if (marker.StartsWith(prefix, StringComparison.Ordinal) && marker.Length > prefix.Length)
                return marker[prefix.Length..];   // [a..] = il testo dalla posizione a fino alla fine
        return null;
    }

    public static bool IsPlaceholder(string marker) =>
        marker is Placeholder or SetupBoilRPlaceholder || marker.StartsWith(Placeholder + ":", StringComparison.Ordinal);

    public static bool IsSteamAll(string marker) =>
        marker is SteamAll or Placeholder
        || marker.StartsWith(SteamAll + ":", StringComparison.Ordinal)
        || marker.StartsWith(Placeholder + ":", StringComparison.Ordinal);
}
