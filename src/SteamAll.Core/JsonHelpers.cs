// =====================================================================================
//  JsonHelpers.cs
//  Piccoli aiuti per leggere JSON "di altri" (la cache di Epic, il database di GOG), dove un
//  campo può mancare o avere un tipo inatteso: invece di errori vogliamo valori vuoti.
//
//  Usiamo System.Text.Json, la libreria JSON inclusa in .NET. JsonNode è un albero modificabile:
//    { ... } -> JsonObject     [ ... ] -> JsonArray     "testo", 12, true -> JsonValue
// =====================================================================================

using System.Text.Json;
using System.Text.Json.Nodes;

namespace SteamAll.Core;

// "internal": visibile solo dentro SteamAll.Core (e nelle prove, vedi SteamAll.Core.csproj).
internal static class JsonHelpers
{
    /// <summary>Legge un testo JSON; null se è vuoto o rovinato.</summary>
    public static JsonNode? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // I metodi qui sotto sono "metodi di estensione": il "this" davanti al primo parametro permette
    // di chiamarli come se fossero metodi di JsonNode:  nodo.Text("title")  invece di
    // JsonHelpers.Text(nodo, "title"). Funzionano anche su un nodo null (danno un valore vuoto).

    /// <summary>Il campo di un oggetto JSON (null se il nodo non è un oggetto o il campo manca).</summary>
    public static JsonNode? Field(this JsonNode? node, string key) =>
        node is JsonObject obj ? obj[key] : null;

    /// <summary>Un campo come testo ("" se manca). Anche i numeri diventano testo.</summary>
    public static string Text(this JsonNode? node, string key) =>
        node.Field(key) switch
        {
            // Un "pattern" con condizione: se è un JsonValue e contiene un testo, quel testo.
            JsonValue v when v.TryGetValue(out string? s) => s,
            JsonValue v => v.ToJsonString(),   // un numero, true/false: la sua forma in JSON
            _ => "",
        };

    /// <summary>Il nodo stesso come testo, se è un valore di testo ("" altrimenti).</summary>
    public static string AsText(this JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out string? s) ? s : "";

    /// <summary>Un campo vero/falso (false se manca o non è un booleano).</summary>
    public static bool Flag(this JsonNode? node, string key) =>
        node.Field(key) is JsonValue v && v.TryGetValue(out bool b) && b;

    /// <summary>
    /// Gli elementi di un array: quello del campo "key", o il nodo stesso se key è null.
    /// Vuoto se non è un array. OfType toglie anche gli elementi null.
    /// </summary>
    public static IEnumerable<JsonNode> Items(this JsonNode? node, string? key = null) =>
        (key is null ? node : node.Field(key)) is JsonArray array ? array.OfType<JsonNode>() : [];
}
