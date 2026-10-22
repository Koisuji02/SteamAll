// =====================================================================================
//  LibraryView.axaml.cs
//  La metà in C# della pagina Library. Tutto il lavoro lo fa LibraryViewModel: qui c'è solo
//  il costruttore, che costruisce i controlli descritti nel file .axaml.
// =====================================================================================

using Avalonia.Controls;

namespace SteamAll.Views;

public partial class LibraryView : UserControl
{
    public LibraryView() => InitializeComponent();
}
