// =====================================================================================
//  SourcesViewModel.cs
//  La pagina "Games & emulators": da dove vengono i giochi che non sono degli store.
//    - Le cartelle dei giochi: una sottocartella per gioco (emulatore portable + gioco, o gioco PC).
//    - Gli emulatori installati: il programma dell'emulatore e una o più cartelle di ROM; ogni
//      emulatore prende solo i file del suo sistema (come Steam ROM Manager).
//  Più il registro di cosa è stato letto. Ogni modifica si salva subito e rilegge le fonti.
// =====================================================================================

using System.Collections.ObjectModel;
using SteamAll.Core.Games;
using SteamAll.Core.Plan;
using SteamAll.Core.Settings;

namespace SteamAll.ViewModels;

/// <summary>Una cartella dei giochi nella pagina: nome, cosa contiene, spunta, pulsante per toglierla.</summary>
public sealed class SourceItemViewModel(MainWindowViewModel shell, string name, string summary) : ViewModelBase
{
    public string Name { get; } = name;
    public string Summary { get; } = summary;

    public bool IsEnabled
    {
        get => !shell.Settings.ExcludedSources.Contains(Name);
        set
        {
            if (value == IsEnabled) return;
            shell.SetSourceEnabled(Name, value);
            OnPropertyChanged();
        }
    }

    // "??=" crea il comando solo la prima volta che serve.
    public RelayCommand RemoveCommand => removeCommand ??= new RelayCommand(() => shell.RemoveFolder(Name));
    private RelayCommand? removeCommand;
}

/// <summary>Una cartella di ROM di un emulatore, con il suo pulsante "togli".</summary>
public sealed record RomFolderItem(string Path, RelayCommand RemoveCommand);

/// <summary>Un emulatore installato nella pagina.</summary>
public sealed class EmulatorSetupViewModel : ViewModelBase
{
    private readonly MainWindowViewModel shell;

    public EmulatorSetupViewModel(MainWindowViewModel shell, EmulatorSetup setup, SourceResult? result)
    {
        this.shell = shell;
        Setup = setup;
        Summary = result switch
        {
            null when !setup.Enabled => "Turned off: its games are not imported, and those already in Steam are removed on Apply.",
            null => setup.ExePath.Length == 0 ? "Choose the emulator's program." : "",
            { Error: { } error } => $"Can't be read: {error}",
            { Games: { } games } => games.Count == 0 ? "No games for this emulator in its folders yet." : $"{games.Count} game{(games.Count == 1 ? "" : "s")} found",
            _ => "",
        };
        foreach (string folder in setup.RomFolders)
            RomFolders.Add(new RomFolderItem(folder, new RelayCommand(() => RemoveRomFolder(folder))));
        RemoveCommand = new RelayCommand(() => shell.RemoveEmulator(setup));
    }

    public EmulatorSetup Setup { get; }

    /// <summary>Gli emulatori del catalogo, per il menu a tendina.</summary>
    public IReadOnlyList<Emulator> Emulators => Emulator.All;

    public Emulator? SelectedEmulator
    {
        get => Setup.Emulator;
        set
        {
            if (value is null || value.Id == Setup.EmulatorId) return;
            Setup.EmulatorId = value.Id;
            Setup.Arguments = "";   // i parametri dell'emulatore di prima non valgono più
            shell.OnEmulatorsChanged();
        }
    }

    public string ExePath => Setup.ExePath.Length > 0 ? Setup.ExePath : "No program chosen";

    /// <summary>Le estensioni che prende: ".nsp .xci".</summary>
    public string Takes => Setup.Emulator is { } e ? "Takes: " + string.Join(" ", e.Extensions.Where(x => x != ".bin").DefaultIfEmpty("PS3 games (EBOOT.BIN)")) : "";

    /// <summary>I parametri di avvio; vuoto = quelli consigliati (mostrati come suggerimento).</summary>
    public string Arguments
    {
        get => Setup.Arguments;
        set
        {
            if (value == Setup.Arguments) return;
            Setup.Arguments = value;
            shell.SaveSettings();   // si usano all'Apply: non serve rileggere
            OnPropertyChanged();
        }
    }

    public string ArgumentsHint => Setup.Emulator?.Arguments ?? "\"{rom}\"";

    public bool Enabled
    {
        get => Setup.Enabled;
        set
        {
            if (value == Setup.Enabled) return;
            Setup.Enabled = value;
            shell.OnEmulatorsChanged();
        }
    }

    public string Summary { get; }

    public ObservableCollection<RomFolderItem> RomFolders { get; } = [];

    public RelayCommand RemoveCommand { get; }

    /// <summary>Il programma scelto con la finestra del sistema: se lo riconosciamo, scegliamo anche l'emulatore.</summary>
    public void SetExe(string path)
    {
        Setup.ExePath = path;
        if (Emulator.Recognize(path) is { } recognized && recognized.Id != Setup.EmulatorId)
        {
            Setup.EmulatorId = recognized.Id;
            Setup.Arguments = "";
        }
        shell.OnEmulatorsChanged();
    }

    public void AddRomFolder(string path)
    {
        if (Setup.RomFolders.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        Setup.RomFolders.Add(path);
        shell.OnEmulatorsChanged();
    }

    private void RemoveRomFolder(string path)
    {
        Setup.RomFolders.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        shell.OnEmulatorsChanged();
    }
}

public sealed class SourcesViewModel : PageViewModel
{
    private readonly MainWindowViewModel shell;

    public SourcesViewModel(MainWindowViewModel shell)
        : base("Games & emulators", "Your game folders, and the emulators installed on this computer with their ROM folders.")
    {
        this.shell = shell;
        AddEmulatorCommand = new RelayCommand(shell.AddEmulator);
    }

    public override Avalonia.Media.Geometry Icon => Icons.Sources;

    public ObservableCollection<SourceItemViewModel> Folders { get; } = [];
    public ObservableCollection<EmulatorSetupViewModel> Emulators { get; } = [];
    public ObservableCollection<string> Log { get; } = [];
    public bool HasNoEmulators => Emulators.Count == 0;

    public RelayCommand AddEmulatorCommand { get; }

    public void SetPlan(ImportPlan plan)
    {
        Folders.Clear();
        foreach (SourceResult s in plan.Sources.Where(s => s.Kind == SourceKind.Folder))
            Folders.Add(new SourceItemViewModel(shell, s.Name, Describe(s)));
        Log.Clear();
        foreach (string line in plan.Log) Log.Add(line);
        SetEmulators(plan);
    }

    /// <summary>Ridisegna gli emulatori (anche senza un piano nuovo, per esempio appena ne aggiungi uno).</summary>
    public void SetEmulators(ImportPlan? plan)
    {
        Emulators.Clear();
        foreach (EmulatorSetup setup in shell.Settings.Emulators)
            Emulators.Add(new EmulatorSetupViewModel(shell, setup, plan?.Sources.FirstOrDefault(s => s.Kind == SourceKind.Emulator && s.Id == setup.Id)));
        OnPropertyChanged(nameof(HasNoEmulators));
    }

    /// <summary>Aggiunge una cartella dei giochi (la sceglie la View con la finestra del sistema) e rilegge tutto.</summary>
    public void AddFolder(string path) => shell.AddFolder(path);

    // "8 games · 2 skipped", oppure il motivo dell'errore.
    private static string Describe(SourceResult s)
    {
        if (s.Error is not null) return $"Can't be read: {s.Error}";
        List<Game> games = s.Games!;
        string text = $"{games.Count} games";
        int skipped = games.Count(g => g.Problem is not null);
        if (skipped > 0) text += $" · {skipped} skipped";
        return text;
    }
}
