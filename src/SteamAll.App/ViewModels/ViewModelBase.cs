// =====================================================================================
//  ViewModelBase.cs
//  La base di tutti i "view model".
//
//  Come è organizzata l'interfaccia (lo schema si chiama MVVM, Model-View-ViewModel):
//    - Model: i dati veri (il nucleo: PlanEntry, AppSettings...).
//    - View: com'è fatta la finestra (i file .axaml): colori, posizioni, controlli.
//    - ViewModel: quello che la finestra mostra e i comandi che offre, in C#, senza sapere
//      niente di colori o posizioni. La View si "lega" alle sue proprietà con {Binding Nome}.
//  Quando una proprietà del ViewModel cambia, deve avvisare la View perché si aggiorni: lo fa
//  con l'evento PropertyChanged dell'interfaccia INotifyPropertyChanged, che qui sotto
//  scriviamo una volta per tutte.
// =====================================================================================

using System.ComponentModel;                 // INotifyPropertyChanged, PropertyChangedEventArgs
using System.Runtime.CompilerServices;       // [CallerMemberName]

namespace SteamAll.ViewModels;

// "abstract" = non si crea da sola: è solo una base da cui gli altri view model ereditano.
public abstract class ViewModelBase : INotifyPropertyChanged
{
    // L'evento a cui la View si "abbona" per sapere quando qualcosa cambia.
    // "event" = un elenco di metodi da chiamare quando succede qualcosa.
    public event PropertyChangedEventHandler? PropertyChanged;

    // [CallerMemberName]: se non passi il nome, il compilatore mette da solo il nome della
    // proprietà da cui chiami il metodo. Così si scrive OnPropertyChanged() e basta.
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));   // "?." = solo se qualcuno è abbonato

    /// <summary>
    /// Cambia un campo e avvisa la View, ma solo se il valore è davvero diverso.
    /// "&lt;T&gt;" = il metodo vale per qualunque tipo (generics); "ref" = riceve il campo stesso.
    /// </summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
