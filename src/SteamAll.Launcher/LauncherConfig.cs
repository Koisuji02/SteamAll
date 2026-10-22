// =====================================================================================
//  LauncherConfig.cs
//  Le impostazioni di un launcher: <cartella del launcher>\SteamAll\launcher.ini, scritto da SteamAll.
//  Un file di testo con righe "chiave=valore" (le righe che iniziano con ";" sono commenti):
//      Emulator=Ryujinx.exe                     il programma dell'emulatore
//      Game=games\Kirby [0100227010460000].nsp  il file del gioco ("" = lo dice Steam, nelle opzioni di avvio)
//      Arguments="{rom}"                        i parametri: {rom} diventa il file del gioco
//      Controller=Ryujinx                       configurazione del controller gestita ("" = nessuna)
//      ConfigDir=portable                       dove sta la configurazione del controller dell'emulatore
//      DataDir=SteamAll                         dove stanno le configurazioni messe da parte e state.ini
//  I percorsi possono essere relativi alla cartella del launcher (quella con l'exe del gioco, o la
//  cartella del launcher di un emulatore installato) oppure completi.
//  Anche questo file è condiviso tra launcher e nucleo.
// =====================================================================================

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SteamAll.Launcher;

public static class LauncherConfig
{
    public const string FileName = "launcher.ini";

    public static Dictionary<string, string> Read(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return values;
        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
        {
            int i = line.IndexOf('=');
            if (i > 0 && !line.StartsWith(";")) values[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
        }
        return values;
    }

    /// <summary>Il testo del file (per scriverlo, e per capire se è già aggiornato).</summary>
    public static string Format(string emulator, string game, string arguments, string controller, string configDir, string dataDir)
    {
        var sb = new StringBuilder();
        sb.Append("; SteamAll - settings of this game's launcher. Written by SteamAll: don't edit, press Apply in SteamAll.\r\n");
        sb.Append("Emulator=").Append(emulator).Append("\r\n");
        sb.Append("Game=").Append(game).Append("\r\n");
        sb.Append("Arguments=").Append(arguments).Append("\r\n");
        sb.Append("Controller=").Append(controller).Append("\r\n");
        sb.Append("ConfigDir=").Append(configDir).Append("\r\n");
        sb.Append("DataDir=").Append(dataDir).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>Un valore del file ("" se manca).</summary>
    public static string Value(Dictionary<string, string> config, string key)
    {
        string? value;
        return config.TryGetValue(key, out value) && value != null ? value : "";
    }

    /// <summary>Un percorso del file: relativo alla cartella del launcher, o completo. "" resta "".</summary>
    public static string Resolve(string baseDir, string path) =>
        path.Length == 0 ? "" : Path.GetFullPath(Path.Combine(baseDir, path));

    /// <summary>
    /// I parametri: dal campo Arguments, oppure (file di una versione precedente) da ArgsBefore e
    /// ArgsAfter, con il gioco in mezzo.
    /// </summary>
    public static string Arguments(Dictionary<string, string> config)
    {
        if (config.ContainsKey("Arguments")) return Value(config, "Arguments");
        string before = Value(config, "ArgsBefore"), after = Value(config, "ArgsAfter");
        return ((before.Length > 0 ? before + " " : "") + "\"{rom}\"" + (after.Length > 0 ? " " + after : "")).Trim();
    }

    /// <summary>La riga di comando dell'emulatore: i parametri con il gioco al posto di {rom}.</summary>
    public static string CommandLine(string arguments, string rom) =>
        arguments.Contains("{rom}") ? arguments.Replace("{rom}", rom) : (arguments + " \"" + rom + "\"").Trim();
}
