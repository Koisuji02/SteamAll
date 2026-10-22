// =====================================================================================
//  PageViewModel.cs
//  La base delle "pagine" del menu a sinistra (Library, Stores, Games & emulators, Controllers,
//  Recommended, Settings).
// =====================================================================================

using Avalonia.Media;   // Geometry: una forma disegnata con un "percorso" (come nei file SVG)

namespace SteamAll.ViewModels;

public abstract class PageViewModel(string title, string subtitle) : ViewModelBase
{
    /// <summary>Il nome nel menu e in alto nella pagina.</summary>
    public string Title { get; } = title;

    /// <summary>Una riga di spiegazione sotto il titolo della pagina.</summary>
    public string Subtitle { get; } = subtitle;

    /// <summary>L'icona nel menu, disegnata in un quadrato 24x24.</summary>
    // "virtual" = le classi che ereditano possono sostituirla con "override".
    public virtual Geometry Icon => Icons.Library;

    /// <summary>La pagina compare (la scegli nel menu) o sparisce: per esempio Controllers legge il controller solo quando la vedi.</summary>
    public virtual void OnShown() { }
    public virtual void OnHidden() { }
}

/// <summary>
/// Le icone del menu, come "percorsi": M = sposta la penna, H/V = linea orizzontale/verticale,
/// L = linea, C = curva, Z = chiudi la forma. Sono forme semplici disegnate per SteamAll.
/// </summary>
public static class Icons
{
    public static readonly Geometry Library = StreamGeometry.Parse("M3,3H10V10H3Z M14,3H21V10H14Z M3,14H10V21H3Z M14,14H21V21H14Z");
    // Un negozio: la tenda in alto e la vetrina sotto.
    public static readonly Geometry Stores = StreamGeometry.Parse("M3,4H21L22,9C22,10.5 20.8,11.5 19.5,11.5C18.2,11.5 17,10.5 17,9C17,10.5 15.8,11.5 14.5,11.5C13.2,11.5 12,10.5 12,9C12,10.5 10.8,11.5 9.5,11.5C8.2,11.5 7,10.5 7,9C7,10.5 5.8,11.5 4.5,11.5C3.2,11.5 2,10.5 2,9Z M4,13H20V21H4Z");
    // Una stella a cinque punte.
    public static readonly Geometry Recommended = StreamGeometry.Parse("M12,2L14.9,8.6L22,9.2L16.6,13.9L18.2,21L12,17.3L5.8,21L7.4,13.9L2,9.2L9.1,8.6Z");
    public static readonly Geometry Sources = StreamGeometry.Parse("M2,5H9L11,7H22V20H2Z");
    public static readonly Geometry Controllers = StreamGeometry.Parse(
        "M7,7H17C20,7 22,9.5 22,13C22,16 20.5,18 18.5,18C17,18 16,17 15,15.5H9C8,17 7,18 5.5,18C3.5,18 2,16 2,13C2,9.5 4,7 7,7Z");
    public static readonly Geometry Settings = StreamGeometry.Parse("M3,5H21V7H3Z M3,11H21V13H3Z M3,17H21V19H3Z M7,3H10V9H7Z M14,9H17V15H14Z M9,15H12V21H9Z");
}
