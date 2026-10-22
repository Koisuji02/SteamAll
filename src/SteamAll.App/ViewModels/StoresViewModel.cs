// =====================================================================================
//  StoresViewModel.cs
//  La pagina "Stores": una scheda quadrata per store, con l'icona grande, il nome, il pulsante
//  Install (apre il sito per scaricarlo; "Installed" e spento se c'è già) e l'interruttore
//  Used / Not used. Spegnere uno store: i suoi giochi non si importano più, e quelli già in Steam
//  (installati e da installare) si tolgono al prossimo Apply, come fa BoilR con gli store che non ci
//  sono più.
// =====================================================================================

using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using SteamAll.Core.Plan;
using SteamAll.Services;

namespace SteamAll.ViewModels;

/// <summary>Uno store nella pagina.</summary>
public sealed class StoreItemViewModel : ViewModelBase
{
    private readonly MainWindowViewModel shell;
    private Bitmap? icon;

    public StoreItemViewModel(MainWindowViewModel shell, StoreReport report)
    {
        this.shell = shell;
        Report = report;
        InstallCommand = new RelayCommand(() => MainWindowViewModel.OpenLink(report.Info.Website), () => !IsInstalled);
        _ = LoadIconAsync();
    }

    public StoreReport Report { get; }
    public string Name => Report.Info.Name;
    public bool IsInstalled => Report.Status != StoreStatus.NotInstalled;
    public string InstallText => IsInstalled ? "Installed" : "Install";
    public RelayCommand InstallCommand { get; }

    /// <summary>Le iniziali, finché (o se) l'icona non c'è: "GOG GALAXY" -> "GG".</summary>
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                                .Where(w => char.IsLetterOrDigit(w[0])).Take(2)
                                                .Select(w => char.ToUpperInvariant(w[0])));

    public Bitmap? Icon
    {
        get => icon;
        private set
        {
            if (SetField(ref icon, value)) OnPropertyChanged(nameof(HasIcon));
        }
    }

    public bool HasIcon => icon is not null;

    /// <summary>L'interruttore: acceso = i giochi di questo store vanno in Steam.</summary>
    public bool IsEnabled
    {
        get => Report.Status == StoreStatus.Active;
        set
        {
            if (!IsInstalled || value == IsEnabled) return;
            shell.SetStoreEnabled(Report.Info.Id, value);   // salva e rilegge tutto
        }
    }

    /// <summary>Il suggerimento sulla scheda: cosa si è letto, o l'errore.</summary>
    public string Tooltip => Report.Result switch
    {
        { Error: { } error } => $"Can't be read: {error}",
        { Games: { } games } => $"{games.Count} games, {games.Count(g => g.IsInstalled)} installed",
        _ => IsInstalled ? "Turned off: its games stay out of Steam" : "Not installed on this computer",
    };

    private async Task LoadIconAsync() =>
        Icon = await StoreIconLoader.LoadAsync(Report.Info.Id, Report.Info.IconUrl, Report.Info.Protocol, shell.Platform);
}

public sealed class StoresViewModel(MainWindowViewModel shell)
    : PageViewModel("Stores", "Their libraries are read from the store apps on this computer: no login.")
{
    public override Avalonia.Media.Geometry Icon => Icons.Stores;

    public ObservableCollection<StoreItemViewModel> Stores { get; } = [];
    public bool HasNoStores => Stores.Count == 0;

    public void SetPlan(ImportPlan plan)
    {
        Stores.Clear();
        foreach (StoreReport r in plan.Stores) Stores.Add(new StoreItemViewModel(shell, r));
        OnPropertyChanged(nameof(HasNoStores));
    }
}
