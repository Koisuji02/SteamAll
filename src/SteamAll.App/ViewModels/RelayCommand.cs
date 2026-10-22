// =====================================================================================
//  RelayCommand.cs
//  Un "comando": quello che succede quando premi un pulsante. In XAML si scrive
//  Command="{Binding RefreshCommand}" e Avalonia chiama Execute al clic. CanExecute dice se il
//  pulsante è attivo: quando torna false, il pulsante si disattiva da solo.
// =====================================================================================

using System.Windows.Input;   // ICommand: l'interfaccia dei comandi, comune a tutte le interfacce .NET

namespace SteamAll.ViewModels;

// Il costruttore primario riceve due metodi: cosa fare (execute) e, facoltativo, quando si può
// fare (canExecute). "Func<bool>? canExecute = null" = parametro facoltativo, null se non lo passi.
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();

    /// <summary>Da chiamare quando cambia qualcosa che decide se il comando si può usare.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
