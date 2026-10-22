// =====================================================================================
//  SteamInputLayouts.cs
//  Un controllo (solo lettura) dei layout di Steam Input dei tuoi giochi: due tasti frontali
//  legati allo stesso tasto (es. A e B che mandano entrambi "A") sono un layout sbagliato in
//  Steam, che nessuna configurazione dell'emulatore può correggere. È successo davvero con
//  Mario + Rabbids. Steam salva i layout per nome del gioco, in minuscolo:
//    Steam\steamapps\common\Steam Controller Configs\<utente>\config\<nome>\controller_*.vdf
// =====================================================================================

using System.Text.RegularExpressions;

namespace SteamAll.Core.Controllers;

/// <summary>Un layout sbagliato: il gioco, il tipo di controller e cosa non va.</summary>
public sealed record LayoutWarning(string Game, string Controller, string Problem);

public static class SteamInputLayouts
{
    // I file controller_<tipo>.vdf: il tipo dice per quale controller è il layout.
    private static readonly (string Prefix, string Name)[] Types =
    [
        ("switch", "Joy-Con / Switch Pro"), ("xbox", "Xbox"), ("ps4", "PlayStation"), ("ps5", "PlayStation"),
        ("neptune", "Steam Deck"), ("steamcontroller", "Steam Controller"),
    ];

    public static List<LayoutWarning> Check(string steamFolder, string userId, IEnumerable<string> gameNames)
    {
        var warnings = new List<LayoutWarning>();
        string root = Path.Combine(steamFolder, "steamapps", "common", "Steam Controller Configs", userId, "config");
        foreach (string game in gameNames)
        {
            string dir = Path.Combine(root, game.ToLowerInvariant());
            if (!Directory.Exists(dir)) continue;
            foreach (string file in Directory.GetFiles(dir, "controller_*.vdf"))
            {
                string text = File.ReadAllText(file);
                string type = Path.GetFileNameWithoutExtension(file)["controller_".Length..];
                string controller = Types.FirstOrDefault(t => type.StartsWith(t.Prefix, StringComparison.OrdinalIgnoreCase)).Name ?? type;
                warnings.AddRange(DoubleBindings(text).Select(p => new LayoutWarning(game, controller, p)));
            }
        }
        return warnings;
    }

    /// <summary>I tasti frontali doppi nei gruppi attivi di un layout (testo KeyValues di Steam).</summary>
    internal static IEnumerable<string> DoubleBindings(string text)
    {
        // I gruppi "a quattro tasti" (il rombo A/B/X/Y) attivi nei set di azioni.
        HashSet<string> active = Regex.Matches(text, @"""(\d+)""\s*""button_diamond active""").Select(m => m.Groups[1].Value).ToHashSet();
        foreach (string group in Regex.Split(text, @"(?=""group""\s*\{)"))
        {
            Match id = Regex.Match(group, @"^""group""\s*\{\s*""id""\s*""(\d+)""[^{}]*?""mode""\s*""four_buttons""");
            if (!id.Success || !active.Contains(id.Groups[1].Value)) continue;

            // Ogni tasto (button_a...) -> il tasto Xbox che manda (il primo "binding").
            var bindings = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(group, @"""(button_[abxy])""\s*\{(?:(?!""button_).)*?""binding""\s*""xinput_button ([A-Za-z]+)", RegexOptions.Singleline))
                bindings.TryAdd(m.Groups[1].Value, m.Groups[2].Value.ToUpperInvariant());

            foreach (IGrouping<string, KeyValuePair<string, string>> same in bindings.GroupBy(b => b.Value).Where(g => g.Count() > 1))
            {
                string buttons = string.Join(" and ", same.Select(b => b.Key["button_".Length..].ToUpperInvariant()).Order());
                yield return $"{buttons} all send {same.Key}: fix it in Steam (game > controller icon > Edit layout > Buttons)";
                // "yield return" = restituisce un elemento alla volta: il metodo è un "iteratore".
            }
        }
    }
}
