// =====================================================================================
//  LibraryViewModel.cs
//  La pagina "Library": la griglia delle copertine di tutti i giochi, con filtri per fonte,
//  ricerca per nome, "seleziona tutto / niente" e la spunta su ogni gioco.
// =====================================================================================

using System.Collections.ObjectModel;   // ObservableCollection: una lista che avvisa la View quando cambia
using SteamAll.Core.Plan;
using SteamAll.Services;

namespace SteamAll.ViewModels;

/// <summary>Un filtro della libreria: un'etichetta e la condizione che i giochi devono rispettare.</summary>
public sealed record LibraryFilter(string Label, Func<PlanEntry, bool> Matches);

public sealed class LibraryViewModel : PageViewModel
{
    private readonly MainWindowViewModel shell;   // la finestra principale: salva le scelte, aggiorna il riepilogo
    private List<GameTileViewModel> allTiles = [];
    private LibraryFilter? selectedFilter;
    private string search = "";

    public LibraryViewModel(MainWindowViewModel shell)
        : base("Library", "Every game SteamAll found. Untick a game to keep it out of Steam.")
    {
        // ": base(...)" = chiama il costruttore della classe da cui ereditiamo (PageViewModel).
        this.shell = shell;
        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
    }

    /// <summary>Le piastrelle visibili (dopo filtro e ricerca): la griglia mostra queste.</summary>
    public ObservableCollection<GameTileViewModel> Tiles { get; } = [];

    /// <summary>I filtri in alto: "All", una voce per fonte, "Not in Steam yet", "Problems".</summary>
    public ObservableCollection<LibraryFilter> Filters { get; } = [];

    public LibraryFilter? SelectedFilter
    {
        get => selectedFilter;
        set { if (SetField(ref selectedFilter, value)) ApplyFilter(); }
    }

    public string Search
    {
        get => search;
        set { if (SetField(ref search, value)) ApplyFilter(); }
    }

    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }

    /// <summary>Riempie la griglia con un piano nuovo e comincia a caricare le copertine.</summary>
    public void SetPlan(ImportPlan plan)
    {
        allTiles = plan.Entries.Select(e => new GameTileViewModel(e, shell.OnGameToggled, shell.OpenGame)).ToList();

        string? previous = selectedFilter?.Label.Split(" (")[0];   // per ritrovare lo stesso filtro dopo un aggiornamento
        Filters.Clear();
        Filters.Add(Filter("All", _ => true));   // "_" = un parametro che non usiamo
        foreach (SourceResult source in plan.Sources.Where(s => s.Games is { Count: > 0 }))
            Filters.Add(Filter(SourceLabel(source.Name), e => e.Game.Source == source.Name));
        if (plan.Entries.Any(e => e.Game.Source == ImportPlan.LeftoversSource))
            Filters.Add(Filter(ImportPlan.LeftoversSource, e => e.Game.Source == ImportPlan.LeftoversSource));
        Filters.Add(Filter("Not in Steam yet", e => e.Status == SteamStatus.New));
        Filters.Add(Filter("Problems", e => e.Problem is not null));

        // Ritroviamo il filtro di prima (se c'è ancora), altrimenti "All". Questo chiama ApplyFilter.
        selectedFilter = null;
        SelectedFilter = Filters.FirstOrDefault(f => f.Label.Split(" (")[0] == previous) ?? Filters[0];

        _ = LoadCoversAsync(allTiles);   // "_ =" = avviamo il lavoro e non aspettiamo che finisca
    }

    /// <summary>Le piastrelle vanno ridisegnate (per esempio dopo la spunta di una fonte).</summary>
    public void RefreshStatuses()
    {
        foreach (GameTileViewModel tile in allTiles) tile.RefreshStatus();
    }

    // Un filtro con il numero di giochi tra parentesi: "Epic Games (45)".
    private LibraryFilter Filter(string label, Func<PlanEntry, bool> matches) =>
        new($"{label} ({allTiles.Count(t => matches(t.Entry))})", matches);

    // Una cartella si mostra con il suo nome breve ("Games"), gli store con il loro nome.
    private static string SourceLabel(string source) =>
        Path.IsPathRooted(source) ? Path.GetFileName(source.TrimEnd('\\', '/')) + " folder" : source;

    private void ApplyFilter()
    {
        Tiles.Clear();
        foreach (GameTileViewModel tile in allTiles)
        {
            bool matchesFilter = selectedFilter?.Matches(tile.Entry) ?? true;
            bool matchesSearch = search.Length == 0 || tile.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
            if (matchesFilter && matchesSearch) Tiles.Add(tile);
        }
    }

    // Seleziona (o deseleziona) tutti i giochi VISIBILI: con un filtro attivo, solo quelli del filtro.
    private void SetAll(bool value)
    {
        shell.BeginBatch();   // una sola scrittura delle impostazioni alla fine, non una per gioco
        foreach (GameTileViewModel tile in Tiles.Where(t => t.CanToggle)) tile.IsChecked = value;
        shell.EndBatch();
    }

    // Carica tutte le copertine "in parallelo": ogni piastrella mostra la sua appena è pronta.
    // Select crea un lavoro (Task) per piastrella; Task.WhenAll aspetta che finiscano tutti.
    // Dopo "await" il codice torna sul thread dell'interfaccia, quindi impostare Cover è sicuro.
    // (I download sono comunque al massimo 4 alla volta: lo decide CoverLoader.)
    private static async Task LoadCoversAsync(List<GameTileViewModel> tiles) =>
        await Task.WhenAll(tiles.Select(async tile => tile.Cover = await CoverLoader.LoadAsync(tile.Entry, width: 300)));
}
