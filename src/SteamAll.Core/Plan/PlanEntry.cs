// =====================================================================================
//  PlanEntry.cs
//  Una riga del piano: un gioco (da una fonte, oppure una voce rimasta in Steam), la sua voce
//  di Steam se c'è già, e la tua scelta (la spunta). Da tutto questo si calcola lo stato.
// =====================================================================================

using SteamAll.Core.Games;
using SteamAll.Core.Steam;

namespace SteamAll.Core.Plan;

/// <summary>Cosa succederà a un gioco quando applicherai il piano.</summary>
public enum SteamStatus
{
    New,             // non è in Steam: verrà aggiunto
    InSteam,         // è già in Steam: resta com'è
    WillBeRemoved,   // è in Steam ma non deve più esserci: verrà tolto
    NotAdded,        // non è in Steam e, senza spunta, non verrà aggiunto
    Skipped,         // c'è un problema (vedi Problem): non lo tocchiamo
}

public sealed class PlanEntry
{
    /// <summary>Il gioco così come l'ha descritto la sua fonte.</summary>
    public required Game Game { get; init; }

    // --- Lo scrive il piano --------------------------------------------------------------
    // "internal set": da fuori del nucleo si possono solo leggere; le scrive il piano.

    /// <summary>La voce di Steam di questo gioco, o null se non c'è ancora.</summary>
    public Shortcut? Shortcut { get; internal set; }

    /// <summary>Il segnaposto "da installare" da togliere, ora che il gioco è installato.</summary>
    public Shortcut? ReplacedPlaceholder { get; internal set; }

    /// <summary>L'app id: quello della voce esistente, o quello che avrà (0 se non si sa ancora).</summary>
    public uint AppId { get; internal set; }

    /// <summary>Una voce rimasta di un gioco che non c'è più: verrà tolta comunque.</summary>
    public bool MustRemove { get; internal set; }

    /// <summary>La copertina già presente in Steam (cartella grid), o null.</summary>
    public string? CoverFile { get; internal set; }

    /// <summary>Un problema trovato dal piano (doppione, nome già in Steam...) oltre a quelli della fonte.</summary>
    public string? PlanProblem { get; internal set; }

    /// <summary>Un'informazione in più dal piano; se manca, quella della fonte.</summary>
    public string? PlanNote { get; internal set; }

    // --- La tua scelta (la scrive l'interfaccia) -------------------------------------------
    public bool IsChecked { get; set; } = true;        // la spunta del gioco
    public bool IsSourceEnabled { get; set; } = true;  // la spunta della sua fonte

    // --- Calcolati ----------------------------------------------------------------------
    // Proprietà con "=>": si ricalcolano ogni volta che le leggi, quindi sono sempre aggiornate.

    public string? Problem => Game.Problem ?? PlanProblem;
    public string Note => PlanNote ?? Game.Note;
    public bool IsIncluded => IsChecked && IsSourceEnabled;

    /// <summary>Si può cambiare la spunta? No per i giochi saltati e per le voci da togliere comunque.</summary>
    public bool CanToggle => Problem is null && !MustRemove;

    /// <summary>Lo stato: dipende da tutto quello che c'è sopra.</summary>
    public SteamStatus Status
    {
        get
        {
            if (Problem is not null) return SteamStatus.Skipped;
            if (MustRemove) return SteamStatus.WillBeRemoved;
            if (IsIncluded) return Shortcut is null ? SteamStatus.New : SteamStatus.InSteam;

            // Senza spunta: si toglie la sua voce, se c'è ed è una di quelle che gestiamo noi
            // (quelle aggiunte a mano restano). "??" = la prima delle due che non è null.
            Shortcut? existing = Shortcut ?? ReplacedPlaceholder;
            if (existing is null) return SteamStatus.NotAdded;
            return existing.IsManaged ? SteamStatus.WillBeRemoved : SteamStatus.InSteam;
        }
    }

    /// <summary>Il testo dello stato, in inglese per l'interfaccia.</summary>
    public static string StatusText(SteamStatus status) => status switch
    {
        SteamStatus.New => "New",
        SteamStatus.InSteam => "In Steam",
        SteamStatus.WillBeRemoved => "Will be removed",
        SteamStatus.NotAdded => "Not added",
        _ => "Skipped",
    };
}
