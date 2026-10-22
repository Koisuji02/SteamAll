// =====================================================================================
//  GameTileViewModel.cs
//  Una "piastrella" della griglia della libreria: copertina, nome, stato e spunta di un gioco.
//  Avvolge una riga del piano (PlanEntry) e la presenta all'interfaccia.
// =====================================================================================

using Avalonia.Media;                // IBrush, SolidColorBrush, Color
using Avalonia.Media.Imaging;        // Bitmap
using SteamAll.Core.Plan;

namespace SteamAll.ViewModels;

public sealed class GameTileViewModel : ViewModelBase
{
    private readonly Action<GameTileViewModel> onToggled;   // chi avvisare quando cambia la spunta
    private Bitmap? cover;

    /// <param name="onOpen">Cosa fare con un clic sulla copertina: aprire la pagina di dettaglio.</param>
    public GameTileViewModel(PlanEntry entry, Action<GameTileViewModel> onToggled, Action<GameTileViewModel> onOpen)
    {
        Entry = entry;
        this.onToggled = onToggled;
        OpenCommand = new RelayCommand(() => onOpen(this));
    }

    /// <summary>Il clic sulla copertina: la pagina di dettaglio del gioco (la spunta si cambia con la casella).</summary>
    public RelayCommand OpenCommand { get; }

    public PlanEntry Entry { get; }

    public string Name => Entry.Game.Name;

    /// <summary>Sotto il nome: il tipo (es. "Ryujinx (Switch)", "Epic") e se è installato.</summary>
    public string Detail => Entry.Game.IsInstalled ? Entry.Game.Kind : $"{Entry.Game.Kind} · not installed";

    /// <summary>Le iniziali, mostrate finché (o se) la copertina non c'è: "Super Mario Galaxy" -> "SMG".</summary>
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                                .Where(w => char.IsLetterOrDigit(w[0]))
                                                .Take(3)
                                                .Select(w => char.ToUpperInvariant(w[0])));

    public Bitmap? Cover
    {
        get => cover;
        set => SetField(ref cover, value);
    }

    /// <summary>La spunta. Cambiarla aggiorna la riga del piano e avvisa la libreria (che salva).</summary>
    public bool IsChecked
    {
        get => Entry.IsChecked;
        set
        {
            if (!CanToggle || Entry.IsChecked == value) return;
            Entry.IsChecked = value;
            OnPropertyChanged();
            RefreshStatus();
            onToggled(this);
        }
    }

    public bool CanToggle => Entry.CanToggle;

    public string StatusText => PlanEntry.StatusText(Entry.Status);

    /// <summary>Il colore dell'etichetta di stato.</summary>
    public IBrush StatusBrush => Entry.Status switch
    {
        SteamStatus.New => Brush("#2E9E5B"),             // verde: verrà aggiunto
        SteamStatus.InSteam => Brush("#3A6EA5"),         // blu: è già in Steam
        SteamStatus.WillBeRemoved => Brush("#C4473A"),   // rosso: verrà tolto
        _ => Brush("#6B6B6B"),                           // grigio: non aggiunto o saltato
    };

    /// <summary>I giochi che non finiranno in Steam si vedono più chiari.</summary>
    public double Opacity => Entry.Status is SteamStatus.NotAdded or SteamStatus.Skipped or SteamStatus.WillBeRemoved ? 0.45 : 1.0;

    /// <summary>Il suggerimento quando passi con il mouse: il problema, o la nota, o la fonte.</summary>
    public string Tooltip => Entry.Problem ?? (Entry.Note.Length > 0 ? Entry.Note : Entry.Game.Source);

    /// <summary>Da chiamare quando lo stato può essere cambiato (spunta del gioco o della fonte).</summary>
    public void RefreshStatus()
    {
        // nameof(X) = il nome della proprietà X come testo, controllato dal compilatore.
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(Opacity));
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
