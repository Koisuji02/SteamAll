// =====================================================================================
//  IStoreProvider.cs
//  Uno store (GOG GALAXY, Epic, Ubisoft Connect...) visto da SteamAll. Ogni store ha il suo
//  "fornitore", una classe che sa:
//    - se lo store è installato su questo computer (senza, i suoi giochi non si importano: il
//      tasto Installa in Steam aprirebbe uno store che non c'è);
//    - quali giochi possiedi, installati e non, leggendo i file che il programma dello store tiene
//      sul disco: nessun login, nessuna password, come BoilR;
//    - come avviare un gioco installato.
//  I giochi NON installati diventano "segnaposto": il loro link (Game.StoreUri) apre lo store sul gioco.
// =====================================================================================

using SteamAll.Core.Apply;
using SteamAll.Core.Games;

namespace SteamAll.Core.Stores;

/// <summary>Come si presenta uno store nella pagina Stores.</summary>
/// <param name="Id">Identificativo breve e stabile: "gog", "epic", "ubisoft", "ea", "amazon", "itch", "xbox", "lutris".</param>
/// <param name="Website">Dove scaricare il programma dello store (il link "Get it").</param>
/// <param name="IconUrl">L'icona dello store (quella del suo sito).</param>
/// <param name="Protocol">Il tipo di link che apre il programma dello store (es. "goggalaxy"): da lì si
/// trova il suo exe, e la sua icona ad alta risoluzione.</param>
/// <param name="Note">Un limite da sapere (es. "only installed games"), o "".</param>
public sealed record StoreInfo(string Id, string Name, string Website, string IconUrl, string Protocol = "", string Note = "");

public interface IStoreProvider
{
    StoreInfo Info { get; }

    /// <summary>Il programma dello store è installato su questo computer?</summary>
    bool IsInstalled();

    /// <summary>
    /// I giochi che possiedi (Game.Origin = Store, Game.StoreId impostato). Se lo store non si
    /// riesce a leggere lancia un'eccezione: i suoi giochi già in Steam restano come sono.
    /// </summary>
    List<Game> Scan();

    /// <summary>Come avviare un gioco INSTALLATO di questo store, o null (in "reason" il perché).</summary>
    ShortcutSpec? LaunchCommand(Game game, out string reason);
}
