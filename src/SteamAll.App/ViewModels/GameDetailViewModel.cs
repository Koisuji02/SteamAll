// =====================================================================================
//  GameDetailViewModel.cs
//  La pagina di dettaglio di un gioco (si apre con un clic sulla copertina, come in BoilR):
//  nome, logo, store di provenienza, la spunta, e le sue immagini (copertina, hero, banner, logo,
//  icona). Un clic su un'immagine apre le alternative di SteamGridDB: quella che scegli si ricorda
//  (AppSettings.ArtworkChoices) e la scrive in Steam il prossimo Apply.
//  Per ogni immagine si mostra, in quest'ordine: quella scelta da te, quella già in Steam (cartella
//  grid), quella dello store.
// =====================================================================================

using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SteamAll.Core.Artwork;
using SteamAll.Core.Games;
using SteamAll.Core.Plan;
using SteamAll.Services;

namespace SteamAll.ViewModels;

/// <summary>Un'immagine del gioco (un "posto": copertina, hero...) nella pagina di dettaglio.</summary>
public sealed class ArtworkSlotViewModel : ViewModelBase
{
    private Bitmap? image;
    private string hint = "";

    public ArtworkSlotViewModel(ArtworkKind kind, string label, double width, double height, Action<ArtworkSlotViewModel> onChoose)
    {
        Kind = kind;
        Label = label;
        Width = width;
        Height = height;
        ChooseCommand = new RelayCommand(() => onChoose(this));
    }

    public ArtworkKind Kind { get; }
    public string Label { get; }
    public double Width { get; }     // la misura del riquadro nella pagina
    public double Height { get; }
    public RelayCommand ChooseCommand { get; }

    public Bitmap? Image
    {
        get => image;
        set { if (SetField(ref image, value)) OnPropertyChanged(nameof(HasImage)); }
    }

    public bool HasImage => image is not null;

    /// <summary>Da dove viene: "Chosen by you", "In Steam", "From the store", "None yet".</summary>
    public string Hint { get => hint; set => SetField(ref hint, value); }
}

/// <summary>Un'alternativa di SteamGridDB nella finestra di scelta.</summary>
public sealed class ImageChoiceViewModel(GridImage image, double width, double height, Action<GridImage> onChoose) : ViewModelBase
{
    private Bitmap? thumb;

    public GridImage Source { get; } = image;
    public double Width { get; } = width;
    public double Height { get; } = height;
    public string SizeText => Source.Width > 0 ? $"{Source.Width}×{Source.Height}" : "";
    public RelayCommand ChooseCommand { get; } = new(() => onChoose(image));

    public Bitmap? Thumb { get => thumb; set => SetField(ref thumb, value); }
}

public sealed class GameDetailViewModel : ViewModelBase
{
    private readonly MainWindowViewModel shell;
    private Bitmap? storeIcon;
    private ArtworkSlotViewModel? pickerSlot;
    private bool pickerOpen, pickerBusy;
    private string pickerMessage = "";

    public GameDetailViewModel(MainWindowViewModel shell, GameTileViewModel tile)
    {
        this.shell = shell;
        Tile = tile;
        CloseCommand = new RelayCommand(shell.CloseGame);
        ClosePickerCommand = new RelayCommand(() => PickerOpen = false);
        ResetCommand = new RelayCommand(() => { if (pickerSlot is not null) Choose(pickerSlot, null); });

        // Le misure dei riquadri seguono le proporzioni delle immagini di Steam.
        Cover = new ArtworkSlotViewModel(ArtworkKind.Cover, "Cover", 150, 225, OpenPicker);
        Hero = new ArtworkSlotViewModel(ArtworkKind.Hero, "Hero", 465, 150, OpenPicker);
        Banner = new ArtworkSlotViewModel(ArtworkKind.Banner, "Banner", 300, 140, OpenPicker);
        Logo = new ArtworkSlotViewModel(ArtworkKind.Logo, "Logo", 300, 140, OpenPicker);
        Icon = new ArtworkSlotViewModel(ArtworkKind.Icon, "Icon", 140, 140, OpenPicker);
        Slots = [Cover, Hero, Banner, Logo, Icon];
        foreach (ArtworkSlotViewModel slot in Slots) _ = LoadSlotAsync(slot);
        _ = LoadStoreIconAsync();
    }

    public GameTileViewModel Tile { get; }
    private Game Game => Tile.Entry.Game;

    public string Name => Game.Name;
    public string KindText => Tile.Detail;
    public string StatusText => Tile.StatusText;
    public IBrush StatusBrush => Tile.StatusBrush;

    /// <summary>Da dove viene: il nome dello store, o la cartella / l'emulatore.</summary>
    public string SourceText => Game.Origin switch
    {
        GameOrigin.Folder => Path.GetFileName(Game.Source.TrimEnd('\\', '/')) is { Length: > 0 } f ? $"{f} folder" : Game.Source,
        _ => Game.Source,
    };

    /// <summary>Il simbolo della provenienza quando non c'è l'icona di uno store.</summary>
    public Geometry SourceGlyph => Game.Origin == GameOrigin.Emulator ? Icons.Controllers : Icons.Sources;

    public Bitmap? StoreIcon
    {
        get => storeIcon;
        private set { if (SetField(ref storeIcon, value)) OnPropertyChanged(nameof(HasStoreIcon)); }
    }

    public bool HasStoreIcon => storeIcon is not null;
    public bool HasNoStoreIcon => storeIcon is null;

    public bool IsChecked
    {
        get => Tile.IsChecked;
        set
        {
            Tile.IsChecked = value;   // la piastrella salva la scelta e ridisegna lo stato
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrush));
        }
    }

    public bool CanToggle => Tile.CanToggle;

    public ArtworkSlotViewModel Cover { get; }
    public ArtworkSlotViewModel Hero { get; }
    public ArtworkSlotViewModel Banner { get; }
    public ArtworkSlotViewModel Logo { get; }
    public ArtworkSlotViewModel Icon { get; }
    public IReadOnlyList<ArtworkSlotViewModel> Slots { get; }

    public RelayCommand CloseCommand { get; }

    // --- Le immagini mostrate ---------------------------------------------------------------

    private string? ChosenUrl(ArtworkKind kind) =>
        shell.Settings.ArtworkChoices.TryGetValue(Game.Key, out Dictionary<string, string>? c) && c.TryGetValue(kind.ToString(), out string? url) ? url : null;

    private async Task LoadSlotAsync(ArtworkSlotViewModel slot)
    {
        int width = (int)(slot.Width * 2);   // il doppio: nitide anche con lo schermo ingrandito
        string? grid = shell.Plan?.Library?.GridFolder;
        string? inSteam = grid is not null && Tile.Entry.AppId != 0 ? ArtworkFiles.Find(grid, Tile.Entry.AppId, slot.Kind) : null;
        if (ChosenUrl(slot.Kind) is { } chosen)
        {
            slot.Hint = "Chosen by you: goes to Steam on Apply";
            slot.Image = await CoverLoader.LoadUrlAsync(chosen, width);
        }
        else if (inSteam is not null)
        {
            slot.Hint = "In Steam";
            slot.Image = await CoverLoader.LoadFileAsync(inSteam, width);
        }
        else if (Game.StoreImages.TryGetValue(ArtworkFiles.StoreKey(slot.Kind), out string? url))
        {
            slot.Hint = "From the store (SteamGridDB first, on Apply)";
            slot.Image = await CoverLoader.LoadUrlAsync(url, width);
        }
        else
        {
            slot.Hint = "Downloaded from SteamGridDB on Apply";
            slot.Image = null;
        }
    }

    private async Task LoadStoreIconAsync()
    {
        StoreReport? store = shell.Plan?.Stores.FirstOrDefault(r => r.Info.Id == Game.StoreId);
        if (store is null) return;
        StoreIcon = await StoreIconLoader.LoadAsync(store.Info.Id, store.Info.IconUrl, store.Info.Protocol, shell.Platform);
        OnPropertyChanged(nameof(HasNoStoreIcon));
    }

    // --- La finestra delle alternative ----------------------------------------------------------

    public bool PickerOpen { get => pickerOpen; private set => SetField(ref pickerOpen, value); }
    public bool PickerBusy { get => pickerBusy; private set => SetField(ref pickerBusy, value); }
    public string PickerMessage { get => pickerMessage; private set => SetField(ref pickerMessage, value); }
    public string PickerTitle => pickerSlot is null ? "" : $"Choose the {pickerSlot.Label.ToLowerInvariant()}";
    public ObservableCollection<ImageChoiceViewModel> PickerItems { get; } = [];
    public RelayCommand ClosePickerCommand { get; }
    public RelayCommand ResetCommand { get; }

    private async void OpenPicker(ArtworkSlotViewModel slot)
    {
        pickerSlot = slot;
        OnPropertyChanged(nameof(PickerTitle));
        PickerItems.Clear();
        PickerMessage = "";
        PickerOpen = true;
        var client = new SteamGridDbClient(MainWindowViewModel.Http, shell.Settings.GetSteamGridDbKey(shell.Platform));
        if (!client.HasKey)
        {
            PickerMessage = "Add your SteamGridDB key in Settings to choose images.";
            return;
        }
        PickerBusy = true;
        try
        {
            (long Id, string Name)? game = await client.FindGameAsync(Game.Name);
            if (game is null)
            {
                PickerMessage = "SteamGridDB doesn't know this title.";
                return;
            }
            List<GridImage> images = await client.ImagesAsync(game.Value.Id, slot.Kind);
            if (images.Count == 0) PickerMessage = "SteamGridDB has no images of this kind for this game.";
            // Le miniature: proporzioni del tipo di immagine, alte 150 pixel (le icone e i loghi quadrati).
            const double h = 150;
            double w = slot.Kind switch { ArtworkKind.Cover => 100, ArtworkKind.Hero => 465, ArtworkKind.Banner => 320, ArtworkKind.Logo => 240, _ => 150 };
            foreach (GridImage image in images.Take(60))
                PickerItems.Add(new ImageChoiceViewModel(image, w, h, chosen => Choose(slot, chosen.Url)));
            // Le miniature arrivano una dopo l'altra (al massimo 4 download insieme, vedi CoverLoader).
            await Task.WhenAll(PickerItems.ToList().Select(async item =>
                item.Thumb = await CoverLoader.LoadUrlAsync(item.Source.Thumb.Length > 0 ? item.Source.Thumb : item.Source.Url, (int)(item.Width * 2))));
        }
        catch (Exception ex)
        {
            PickerMessage = ex.Message;
        }
        finally
        {
            PickerBusy = false;
        }
    }

    // Ricorda la scelta (url = null: torna automatica) e ridisegna l'immagine.
    private void Choose(ArtworkSlotViewModel slot, string? url)
    {
        Dictionary<string, Dictionary<string, string>> all = shell.Settings.ArtworkChoices;
        if (url is null)
        {
            if (all.TryGetValue(Game.Key, out Dictionary<string, string>? mine))
            {
                mine.Remove(slot.Kind.ToString());
                if (mine.Count == 0) all.Remove(Game.Key);
            }
        }
        else
        {
            if (!all.TryGetValue(Game.Key, out Dictionary<string, string>? mine)) all[Game.Key] = mine = [];
            mine[slot.Kind.ToString()] = url;
        }
        shell.SaveSettings();
        PickerOpen = false;
        _ = LoadSlotAsync(slot);
        if (slot.Kind == ArtworkKind.Cover) _ = ReloadTileCoverAsync(url);
    }

    // La copertina scelta compare subito anche nella griglia della libreria.
    private async Task ReloadTileCoverAsync(string? url) =>
        Tile.Cover = url is not null ? await CoverLoader.LoadUrlAsync(url, 300) : await CoverLoader.LoadAsync(Tile.Entry, 300);
}
