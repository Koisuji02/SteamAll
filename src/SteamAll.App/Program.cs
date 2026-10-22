// =====================================================================================
//  Program.cs
//  Il punto di ingresso: il primo codice che parte quando apri SteamAll.exe.
//  Qui si prepara Avalonia e si avvia l'applicazione (la classe App, in App.axaml.cs).
// =====================================================================================

using Avalonia;         // AppBuilder e i metodi di configurazione
using Avalonia.Media;   // FontManagerOptions

namespace SteamAll;

// "internal" = visibile solo dentro questo progetto. "static" = non si crea con "new".
internal static class Program
{
    // Main è il metodo da cui parte ogni programma .NET; "args" sono i parametri della riga di comando.
    // [STAThread] è un "attributo" (un'etichetta): su Windows le finestre vogliono che il thread
    // principale lavori in modalità "single-threaded apartment" (serve per appunti, trascinamento...).
    [STAThread]
    public static int Main(string[] args)
    {
        // "SteamAll --launch <launcher.ini> [gioco]": su Linux e macOS è così che Steam avvia i giochi
        // emulati. Niente finestra: SteamAll fa il launcher (controller + emulatore) ed esce.
        if (args.Length > 0 && args[0] == "--launch") return Core.Launchers.GameLauncher.Run(args);

        // Avvia l'app in modalità "desktop classica": una finestra principale; quando la chiudi,
        // il programma finisce. Il metodo resta qui finché l'app è aperta.
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // La configurazione di Avalonia. È un metodo a parte (e pubblico) perché lo usa anche
    // l'anteprima delle finestre nell'editor, che deve avviare Avalonia senza passare da Main.
    //
    // "=>" seguito da un'espressione è un "metodo a espressione": come scrivere { return ...; }.
    // Le chiamate in fila (.Configure().UsePlatformDetect()...) sono uno stile "fluente": ogni
    // metodo restituisce lo stesso AppBuilder, così si può chiamare il successivo.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()    // <App> = la nostra classe applicazione (parametro di tipo)
            .UsePlatformDetect()       // sceglie da solo il sistema: Windows, Linux o macOS
            // Il carattere Figtree (licenza libera OFL), incluso nel programma: lo stesso di OneTracker.
            // "avares://.../Fonts#Figtree" = la famiglia Figtree tra i font della cartella Assets/Fonts
            // (Regular e Bold: Avalonia sceglie da solo quello giusto per FontWeight).
            .With(new FontManagerOptions { DefaultFamilyName = "avares://SteamAll/Assets/Fonts#Figtree" })
            .LogToTrace();             // i messaggi interni di Avalonia vanno al debugger
}