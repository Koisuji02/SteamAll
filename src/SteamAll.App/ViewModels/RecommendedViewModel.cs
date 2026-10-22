// =====================================================================================
//  RecommendedViewModel.cs
//  La pagina "Recommended": le due cose che completano SteamAll in Steam.
//    1. Millennium (temi e plugin per Steam): il pulsante apre il suo sito per scaricarlo;
//       se c'è già diventa "Installed".
//    2. Il tema consigliato, "Steam 2.0 Modular (Windows Accent)", che segue il colore principale di
//       Windows: SteamAll lo porta con sé e lo installa con un pulsante (dopo una conferma). È una
//       modifica di "Steam 2.0 Modular" di Von-Amadeus, a cui va il merito del tema originale.
// =====================================================================================

using Avalonia.Media;
using SteamAll.Core.Millennium;
using SteamAll.Services;

namespace SteamAll.ViewModels;

public sealed class RecommendedViewModel : PageViewModel
{
    public const string OriginalThemeUrl = "https://github.com/Br1Rol2/Steam2.0-modular";
    private const string MillenniumIconUrl = "https://steambrew.app/icon.svg";

    private readonly MainWindowViewModel shell;
    private ThemeState themeState = ThemeState.MillenniumMissing;
    private Geometry? millenniumLogo;

    public override Geometry Icon => Icons.Recommended;

    public RecommendedViewModel(MainWindowViewModel shell)
        : base("Recommended", "Make Steam look and work its best with SteamAll.")
    {
        this.shell = shell;
        InstallThemeCommand = new RelayCommand(AskToInstallTheme, () => themeState is ThemeState.NotInstalled or ThemeState.Outdated);
        _ = LoadLogoAsync();
        Refresh();
    }

    // --- Millennium -----------------------------------------------------------------------

    public bool MillenniumInstalled => shell.MillenniumInstalled;
    public string MillenniumButtonText => shell.MillenniumInstalled ? "Installed" : "Install";
    public RelayCommand OpenMillenniumCommand => shell.OpenMillenniumCommand;
    public Uri MillenniumUri { get; } = new(MainWindowViewModel.MillenniumWebsite);

    /// <summary>Il logo di Millennium (dalla sua icona SVG), o null finché non arriva.</summary>
    public Geometry? MillenniumLogo
    {
        get => millenniumLogo;
        private set
        {
            if (SetField(ref millenniumLogo, value)) OnPropertyChanged(nameof(HasMillenniumLogo));
        }
    }

    public bool HasMillenniumLogo => millenniumLogo is not null;

    private async Task LoadLogoAsync() => MillenniumLogo = await StoreIconLoader.LoadSvgAsync("millennium", MillenniumIconUrl);

    // --- Il tema ------------------------------------------------------------------------------

    public string ThemeButtonText => themeState switch
    {
        ThemeState.Installed => "Installed",
        ThemeState.Outdated => "Update",
        ThemeState.MillenniumMissing => "Needs Millennium",
        _ => "Install",
    };

    public RelayCommand InstallThemeCommand { get; }
    public Uri OriginalThemeUri { get; } = new(OriginalThemeUrl);

    /// <summary>Rilegge se Millennium e il tema ci sono.</summary>
    public void Refresh()
    {
        string? steam = shell.Platform.FindSteamFolder();
        themeState = steam is null ? ThemeState.MillenniumMissing : ThemeInstaller.Status(steam);
        foreach (string name in (string[])[nameof(MillenniumInstalled), nameof(MillenniumButtonText), nameof(ThemeButtonText)])
            OnPropertyChanged(name);
        InstallThemeCommand.RaiseCanExecuteChanged();
    }

    private void AskToInstallTheme()
    {
        string? steam = shell.Platform.FindSteamFolder();
        if (steam is null) return;
        shell.Dialog.Ask("Install the theme",
            "SteamAll copies the theme (it carries it inside) into Millennium's themes and makes it the active theme " +
            "(a backup of Millennium's config.json is kept; an older copy of the theme goes to the Recycle Bin). " +
            "Steam restarts now if it is open, to load it.",
            "Install", async () =>
            {
                await Task.Run(() => ThemeInstaller.Install(steam, shell.Platform));
                shell.Dialog.AddLine("Theme copied and turned on");
                await shell.RestartSteamAsync();
                shell.Dialog.Message = "Done. Enjoy the new look!";
                Refresh();
            });
    }
}
