// =====================================================================================
//  DialogViewModel.cs
//  La "finestra" che compare sopra tutto il resto per chiedere conferma ("Steam will close...")
//  e poi mostrare l'avanzamento e il risultato. Non è una finestra separata: è un pannello dentro
//  la finestra principale (vedi MainWindow.axaml), che si vede quando IsOpen è true.
// =====================================================================================

using System.Collections.ObjectModel;

namespace SteamAll.ViewModels;

public sealed class DialogViewModel : ViewModelBase
{
    private bool isOpen, isBusy;
    private string title = "", message = "", primaryText = "", secondaryText = "";
    private Func<Task>? primaryAction;

    public DialogViewModel()
    {
        // Il pulsante principale esegue l'azione (es. "Apply"); quello secondario chiude.
        PrimaryCommand = new RelayCommand(() => _ = RunPrimaryAsync(), () => !IsBusy && primaryAction is not null);
        SecondaryCommand = new RelayCommand(Close, () => !IsBusy);
    }

    public bool IsOpen { get => isOpen; private set => SetField(ref isOpen, value); }
    public string Title { get => title; private set => SetField(ref title, value); }
    public string Message { get => message; set => SetField(ref message, value); }
    public string PrimaryText { get => primaryText; private set => SetField(ref primaryText, value); }
    public string SecondaryText { get => secondaryText; private set => SetField(ref secondaryText, value); }

    /// <summary>true mentre l'azione lavora: i pulsanti si disattivano e gira la barra.</summary>
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetField(ref isBusy, value)) return;
            PrimaryCommand.RaiseCanExecuteChanged();
            SecondaryCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasPrimary => primaryAction is not null;

    /// <summary>Le righe dell'avanzamento (cosa sta facendo, passo passo).</summary>
    public ObservableCollection<string> Lines { get; } = [];

    public RelayCommand PrimaryCommand { get; }
    public RelayCommand SecondaryCommand { get; }

    /// <summary>
    /// Chiede conferma. "action" è il lavoro da fare se premi il pulsante principale: un metodo
    /// asincrono (Func&lt;Task&gt;) che può scrivere righe con AddLine.
    /// </summary>
    public void Ask(string title, string message, string confirm, Func<Task> action)
    {
        Title = title;
        Message = message;
        PrimaryText = confirm;
        SecondaryText = "Cancel";
        primaryAction = action;
        Lines.Clear();
        Refresh();
        IsOpen = true;
    }

    /// <summary>Mostra solo un messaggio, con "Close".</summary>
    public void Show(string title, string message)
    {
        Title = title;
        Message = message;
        primaryAction = null;
        SecondaryText = "Close";
        Lines.Clear();
        Refresh();
        IsOpen = true;
    }

    public void AddLine(string line) => Lines.Add(line);

    private async Task RunPrimaryAsync()
    {
        if (primaryAction is null) return;
        Func<Task> action = primaryAction;
        primaryAction = null;          // l'azione si fa una volta sola
        SecondaryText = "Close";
        Refresh();
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Message = "Something went wrong: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Close() => IsOpen = false;

    private void Refresh()
    {
        OnPropertyChanged(nameof(HasPrimary));
        PrimaryCommand.RaiseCanExecuteChanged();
        SecondaryCommand.RaiseCanExecuteChanged();
    }
}
