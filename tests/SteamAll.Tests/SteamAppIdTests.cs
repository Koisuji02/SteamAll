// =========================================================================
//  SteamAppIdTests.cs
//  Prove del calcolo dell'app id. I valori attesi NON sono inventati: sono gli app id che Steam ha davvero nella tua libreria (shortcuts.vdf), per voci create da BoilR. Se il nostro calcolo dà gli stessi numeri, riprenderemo quelle voci senza perdere immagini e raccolte.
// =========================================================================

using SteamAll.Core;

namespace SteamAll.Tests;

public class SteamAppIdTests {
    // [Theory] = una prova che si ripete con dati diversi: una volta per ogni [InlineData]
    // I valori di InlineData arrivano, nell'ordine, nei parametri del metodo
    [Theory]
    [InlineData(@"C:\Games\Kirby and the Forgotten Land\Kirby and the Forgotten Land.exe", "Kirby and the Forgotten Land", 4113688513u)]
    [InlineData(@"C:\Games\Pokémon Sword\Pokémon Sword.exe",   // con una lettera accentata: conta l'UTF-8
                "Pokémon Sword", 4059789564u)]
    [InlineData(@"C:\Games\The Ratchet & Clank Trilogy\The Ratchet & Clank Trilogy.exe",
                "The Ratchet & Clank Trilogy", 2627111528u)]
    [InlineData(@"C:\Program Files\GOG Galaxy\Games\The Witcher 3 Wild Hunt GOTY\REDprelauncher.exe",
                "The Witcher 3: Wild Hunt - Game of the Year Edition", 2420463418u)]
    public void ForShortcut_MatchesTheAppIdsInSteam(string exePath, string name, uint expected) {
        uint actual = SteamAppId.ForShortcut(exePath, name);

        // Assert.Equal(atteso, ottenuto): se sono diversi la prova fallisce e mostra i due valori.
        Assert.Equal(expected, actual);
    }

    // [Fact] = una prova singola, senza dati. Steam distingue i giochi non Steam da quelli Steam proprio dal bit più alto: deve essere sempre acceso, qualunque siano exe e nome
    [Fact]
    public void ForShortcut_AlwaysSetsTheTopBit() {
        uint id = SteamAppId.ForShortcut(@"C:\x.exe", "x");

        Assert.True(id >= 0x8000_0000u);
    }
}