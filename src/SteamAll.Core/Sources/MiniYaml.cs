// =====================================================================================
//  MiniYaml.cs
//  Un lettore minimo di YAML, il formato di testo delle configurazioni dei giochi di Ubisoft
//  Connect. YAML descrive un albero con i rientri (gli spazi a inizio riga):
//
//      root:                      ->  "root" contiene quello che sta più a destra sotto di lui
//        name: l1                 ->  root.name = "l1"
//        addons:
//        - id: 1234               ->  "- " = un elemento di un elenco: root.addons[].id = "1234"
//          is_visible: yes
//      localizations:
//        default:
//          l1: Far Cry 5          ->  localizations.default.l1 = "Far Cry 5"
//
//  Non costruiamo l'albero: "appiattiamo" ogni valore nel suo percorso ("root.addons[].id") e
//  teniamo tutti i valori di ogni percorso. Basta per leggere i pochi campi che ci servono.
//  Non gestisce tutto lo YAML (ancore, mappe su una riga...): non serve per questi file.
// =====================================================================================

using System.Text;

namespace SteamAll.Core.Sources;

internal static class MiniYaml
{
    /// <summary>Percorso -> valori, nell'ordine in cui compaiono.</summary>
    public static Dictionary<string, List<string>> Flatten(string yaml)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        // La "pila" dei contenitori aperti: rientro e nome. In cima c'è il più interno.
        var stack = new List<(int Indent, string Key)>();
        int skipDeeperThan = -1;   // dentro un testo su più righe ("|" o ">"): righe da saltare

        foreach (string raw in yaml.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            string text = line.TrimStart(' ');
            if (text.Length == 0 || text.StartsWith('#')) continue;   // riga vuota o commento
            int indent = line.Length - text.Length;

            if (skipDeeperThan >= 0)
            {
                if (indent > skipDeeperThan) continue;
                skipDeeperThan = -1;
            }

            // "- ..." = un elemento di un elenco: il contenitore è la chiave sopra, con "[]".
            if (text == "-" || text.StartsWith("- ", StringComparison.Ordinal))
            {
                while (stack.Count > 0 && stack[^1].Indent > indent) stack.RemoveAt(stack.Count - 1);
                // [^1] = l'ultimo elemento ("^1" conta dalla fine).
                if (stack.Count > 0 && !stack[^1].Key.EndsWith("[]", StringComparison.Ordinal))
                    stack[^1] = (stack[^1].Indent, stack[^1].Key + "[]");

                // Il contenuto dell'elemento sta "dentro" l'elenco: un livello senza nome (Key "")
                // lo tiene separato dai fratelli dell'elenco, e dagli altri elementi.
                string content = text[1..].TrimStart(' ');
                if (content.Length == 0)
                {
                    stack.Add((indent + 1, ""));   // "-" da solo: il contenuto è nelle righe sotto
                    continue;
                }
                int contentIndent = line.Length - content.Length;
                stack.Add((contentIndent - 1, ""));
                text = content;
                indent = contentIndent;
            }
            else
            {
                while (stack.Count > 0 && stack[^1].Indent >= indent) stack.RemoveAt(stack.Count - 1);
            }

            int colon = FindColon(text);
            string prefix = string.Join(".", stack.Select(s => s.Key).Where(k => k.Length > 0));
            if (colon < 0)
            {
                // Un valore semplice in un elenco ("- valore"): il percorso è quello dell'elenco.
                Add(values, prefix, Unquote(text));
                continue;
            }
            string key = Unquote(text[..colon].Trim());
            string value = text[(colon + 1)..].Trim();
            string path = prefix.Length > 0 ? prefix + "." + key : key;

            if (value.Length == 0)
            {
                stack.Add((indent, key));   // un contenitore: i figli verranno sotto
                continue;
            }
            if (value is "|" or ">" or "|-" or ">-" or "|+" or ">+")
            {
                skipDeeperThan = indent;   // il testo su più righe non ci serve
                Add(values, path, "");
                continue;
            }
            Add(values, path, Unquote(StripComment(value)));
        }
        return values;
    }

    /// <summary>Il primo valore di un percorso, o "".</summary>
    public static string First(this Dictionary<string, List<string>> values, string path) =>
        values.TryGetValue(path, out List<string>? list) && list.Count > 0 ? list[0] : "";

    /// <summary>C'è almeno un valore in questo percorso o sotto di lui?</summary>
    public static bool Has(this Dictionary<string, List<string>> values, string path) =>
        values.Keys.Any(k => k == path || k.StartsWith(path + ".", StringComparison.Ordinal) || k.StartsWith(path + "[]", StringComparison.Ordinal));

    private static void Add(Dictionary<string, List<string>> values, string path, string value)
    {
        if (!values.TryGetValue(path, out List<string>? list)) values[path] = list = [];
        list.Add(value);
    }

    // I due punti che separano chiave e valore: seguiti da uno spazio o a fine riga, fuori dalle virgolette.
    private static int FindColon(string text)
    {
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if ((c == '"' || c == '\'') && i == 0) { quote = c; continue; }
            if (c == ':' && (i + 1 == text.Length || text[i + 1] == ' ')) return i;
        }
        return -1;
    }

    // " # commento" dopo un valore senza virgolette.
    private static string StripComment(string value)
    {
        if (value.StartsWith('"') || value.StartsWith('\'')) return value;
        int hash = value.IndexOf(" #", StringComparison.Ordinal);
        return hash >= 0 ? value[..hash].TrimEnd() : value;
    }

    // 'testo' o "testo" -> testo. Tra apici singoli '' vale un apice; tra virgolette doppie \" e \\.
    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'') return value[1..^1].Replace("''", "'");
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            var sb = new StringBuilder();
            for (int i = 1; i < value.Length - 1; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length - 1)
                {
                    char n = value[++i];
                    sb.Append(n switch { 'n' => '\n', 't' => '\t', _ => n });
                }
                else sb.Append(value[i]);
            }
            return sb.ToString();
        }
        return value;
    }
}
