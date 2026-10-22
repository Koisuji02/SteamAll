// =====================================================================================
//  Names.cs
//  Confronto dei nomi dei giochi. Lo stesso gioco può chiamarsi in modi un po' diversi:
//  "Pokémon™ Sword", "Pokemon Sword", "POKEMON SWORD"... Per capire che sono uguali li
//  riduciamo tutti alla stessa forma: solo lettere e cifre, minuscolo, senza accenti.
// =====================================================================================

using System.Globalization;   // CharUnicodeInfo, UnicodeCategory
using System.Text;            // NormalizationForm, StringBuilder

namespace SteamAll.Core.Games;

public static class Names
{
    /// <summary>"Pokémon™ Sword" -> "pokemonsword".</summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        // FormD "scompone" le lettere accentate: "é" diventa "e" + l'accento come carattere a parte
        // (della categoria NonSpacingMark), che poi scartiamo.
        string decomposed = name.Normalize(NormalizationForm.FormD);

        // StringBuilder costruisce un testo pezzo per pezzo: le stringhe in .NET non si possono
        // modificare, e unirle in un ciclo creerebbe ogni volta una stringa nuova.
        var result = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }
}
