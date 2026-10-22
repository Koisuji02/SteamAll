// =====================================================================================
//  SteamInstall.cs
//  Steam installato su questo PC: i suoi utenti (le cartelle in userdata) e quale usare.
// =====================================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace SteamAll.Core.Steam;

/// <summary>Un utente di Steam su questo PC.</summary>
// "record" = una classe fatta per contenere dati: il costruttore primario crea da solo le
// proprietà (Id, PersonaName...), e due record con gli stessi valori risultano uguali.
public sealed record SteamUser(string Id, string PersonaName, string ConfigFolder, DateTime LastUsedUtc)
{
    // Come appare quando diventa testo (per esempio in un menu a tendina): "player (123456789)".
    // "override" = sostituisce la versione che ogni oggetto eredita.
    public override string ToString() => PersonaName.Length > 0 ? $"{PersonaName} ({Id})" : Id;
}

public sealed class SteamInstall(string folder)
{
    /// <summary>La cartella di Steam, per esempio C:\Program Files (x86)\Steam.</summary>
    public string Folder { get; } = folder;

    /// <summary>Tutti gli utenti: le cartelle di userdata con un nome numerico e una cartella config.</summary>
    public List<SteamUser> GetUsers()
    {
        var users = new List<SteamUser>();
        string userdata = Path.Combine(Folder, "userdata");
        if (!Directory.Exists(userdata)) return users;

        foreach (string dir in Directory.GetDirectories(userdata))
        {
            string id = Path.GetFileName(dir);
            // ^ e $ = inizio e fine: tutto il nome deve essere fatto di cifre (e non iniziare con 0).
            if (!Regex.IsMatch(id, @"^[1-9][0-9]*$")) continue;
            string config = Path.Combine(dir, "config");
            if (!Directory.Exists(config)) continue;

            // localconfig.vdf: la data dell'ultima modifica dice chi ha usato Steam per ultimo, e
            // dentro (è KeyValues in forma di testo) c'è il nome che vedi in Steam.
            string localConfig = Path.Combine(config, "localconfig.vdf");
            DateTime lastUsed = DateTime.MinValue;
            string persona = "";
            if (File.Exists(localConfig))
            {
                lastUsed = File.GetLastWriteTimeUtc(localConfig);
                try
                {
                    string text = Encoding.UTF8.GetString(Files.ReadShared(localConfig));
                    Match m = Regex.Match(text, @"""PersonaName""\s+""([^""]*)""");
                    if (m.Success) persona = m.Groups[1].Value;
                }
                catch (IOException)
                {
                    // non si legge: l'utente resta senza nome, non è un problema
                }
            }
            users.Add(new SteamUser(id, persona, config, lastUsed));
        }
        return users;
    }

    /// <summary>
    /// L'utente da usare: quello indicato (se c'è), altrimenti chi ha usato Steam per ultimo.
    /// Null se non ci sono utenti.
    /// </summary>
    public SteamUser? ChooseUser(string? preferredId)
    {
        List<SteamUser> users = GetUsers();
        // LINQ: FirstOrDefault cerca l'utente con quell'id; MaxBy prende quello con la data più recente.
        return users.FirstOrDefault(u => u.Id == preferredId) ?? users.MaxBy(u => u.LastUsedUtc);
    }
}
