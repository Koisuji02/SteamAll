// =====================================================================================
//  SourcesView.axaml.cs
//  La metà in C# della pagina "Games & emulators". Qui c'è quello che il ViewModel non può fare
//  da solo: aprire le finestre del sistema per scegliere una cartella o un programma (serve la
//  finestra vera).
// =====================================================================================

using Avalonia.Controls;
using Avalonia.Interactivity;      // RoutedEventArgs: i dettagli di un evento come il clic
using Avalonia.Platform.Storage;   // StorageProvider, FolderPickerOpenOptions, FilePickerOpenOptions
using SteamAll.ViewModels;

namespace SteamAll.Views;

public partial class SourcesView : UserControl
{
    public SourcesView() => InitializeComponent();

    // "async void" va bene solo per i gestori di eventi come questi: il clic non aspetta il risultato.
    private async void AddFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SourcesViewModel vm) return;
        if (await PickFolderAsync("Choose a folder with one subfolder per game") is { } path) vm.AddFolder(path);
    }

    // I pulsanti dentro la scheda di un emulatore: il loro DataContext è quell'emulatore.
    private async void AddRomFolder_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not EmulatorSetupViewModel emulator) return;
        if (await PickFolderAsync("Choose a folder with ROMs (subfolders are searched too)") is { } path) emulator.AddRomFolder(path);
    }

    private async void BrowseExe_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not EmulatorSetupViewModel emulator) return;
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        IReadOnlyList<IStorageFile> picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose the emulator's program",
            AllowMultiple = false,
        });
        // TryGetLocalPath = il percorso sul disco; null se il file non è sul disco.
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) emulator.SetExe(path);
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        // La finestra che contiene questa pagina: da lei si apre la scelta della cartella.
        // "is not { } top" = se è null, esci; altrimenti chiamalo top.
        if (TopLevel.GetTopLevel(this) is not { } top) return null;
        IReadOnlyList<IStorageFolder> picked = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }
}
