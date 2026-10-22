// =====================================================================================
//  EmulatorSetup.cs
//  Un emulatore installato nel sistema, come l'hai impostato nella pagina "Games & emulators":
//  quale emulatore è, dov'è il suo programma e in quali cartelle cercare i giochi (le ROM).
//  Si salva nelle impostazioni (AppSettings.Emulators).
// =====================================================================================

using System.Text.Json.Serialization;
using SteamAll.Core.Games;

namespace SteamAll.Core.Settings;

public sealed class EmulatorSetup
{
    /// <summary>Identificativo breve e stabile: dà il nome alla cartella del suo launcher.</summary>
    // Guid.NewGuid() = un numero casuale unico; "N" = senza trattini; [..8] = i primi 8 caratteri.
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>L'emulatore del catalogo (Emulator.Id, es. "dolphin").</summary>
    public string EmulatorId { get; set; } = "";

    /// <summary>Il programma dell'emulatore (exe su Windows, il binario o l'AppImage su Linux).</summary>
    public string ExePath { get; set; } = "";

    /// <summary>Le cartelle dei giochi: si cercano anche nelle sottocartelle.</summary>
    public List<string> RomFolders { get; set; } = [];

    /// <summary>I parametri di avvio; "" = quelli del catalogo. {rom} diventa il file del gioco.</summary>
    public string Arguments { get; set; } = "";

    /// <summary>Spento: i suoi giochi non si importano (e quelli già in Steam si tolgono).</summary>
    public bool Enabled { get; set; } = true;

    // [JsonIgnore]: queste due si calcolano dalle altre, non si salvano nel file.

    /// <summary>L'emulatore del catalogo, o null se l'id non si conosce più.</summary>
    [JsonIgnore]
    public Emulator? Emulator => Games.Emulator.Find(EmulatorId);

    /// <summary>I parametri da usare davvero.</summary>
    [JsonIgnore]
    public string EffectiveArguments => Arguments.Trim().Length > 0 ? Arguments.Trim() : Emulator?.Arguments ?? "\"{rom}\"";
}
