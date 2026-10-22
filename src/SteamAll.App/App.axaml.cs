// =====================================================================================
//  App.axaml.cs
//  La metà in C# della classe App (l'altra metà è App.axaml). Avalonia la chiama all'avvio:
//  prima Initialize (legge il XAML), poi OnFrameworkInitializationCompleted (apre la finestra).
// =====================================================================================

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;   // IClassicDesktopStyleApplicationLifetime
using Avalonia.Markup.Xaml;                     // AvaloniaXamlLoader
using SteamAll.Core.Platform;
using SteamAll.Core.Settings;
using SteamAll.ViewModels;

namespace SteamAll;

// "partial" = la classe è divisa in più file: questo e quello che Avalonia genera da App.axaml.
// ": Application" = App EREDITA da Application: è un'applicazione Avalonia, e noi aggiungiamo il resto.
public partial class App : Application
{
    // "override" = sostituiamo un metodo che Application ha già, per fare la nostra versione.
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);   // carica App.axaml (tema e stili)
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // "is ... desktop" è un controllo di tipo con assegnazione ("pattern matching"): se
        // ApplicationLifetime è un'app desktop, la mette nella variabile "desktop" e entra nell'if.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Le impostazioni salvate (o quelle del primo avvio) e il "cervello" della finestra.
            string settingsPath = AppSettings.DefaultPath;
            var viewModel = new MainWindowViewModel(AppSettings.Load(settingsPath), settingsPath, Platforms.Current);

            // DataContext = da dove la finestra prende i dati per i suoi {Binding ...}.
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
        }

        base.OnFrameworkInitializationCompleted();   // "base" = la versione originale di Application
    }
}
