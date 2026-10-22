// =====================================================================================
//  ControllerSwitch.cs
//  Il cambio automatico della configurazione del controller di un emulatore. Questo file è
//  CONDIVISO: lo compilano sia il launcher (.NET Framework 4.8) sia il nucleo (.NET 10), così il
//  programma e il launcher fanno esattamente la stessa cosa e le prove la controllano. Per questo
//  usa solo cose che esistono in tutte e due le versioni di .NET (niente record, niente intervalli
//  [a..b], niente System.Text.Json).
//
//  L'idea, per ogni emulatore (una copia portable per gioco, o un emulatore installato):
//    - due "configurazioni" del controller messe da parte nella cartella dei dati (dataDir):
//        <dati>\Steam\   per quando il gioco parte da Steam (Steam Input): l'emulatore vede solo
//                        il controller virtuale di Steam, qualunque pad tu usi. SteamAll la
//                        prepara da sé: è il "plug & play".
//        <dati>\Own\     per quando parte fuori da Steam: quella che hai fatto tu nell'emulatore,
//                        così come l'hai lasciata.
//    - prima di avviare l'emulatore, il launcher capisce da dove parte il gioco e, se serve,
//      mette da parte quella in uso (con le modifiche che hai fatto giocando) e mette l'altra.
//    - <dati>\state.ini ricorda quale delle due è in uso (Active=Steam o Active=Own).
//  <dati> è <Gioco>\SteamAll per i giochi delle cartelle, la cartella del launcher per gli
//  emulatori installati.
//
//  I file che contano stanno nella cartella della configurazione dell'emulatore (configDir), che
//  trova SteamAll (vedi ConfigLocator) e scrive nel launcher.ini:
//    Ryujinx  la cartella con Config.json: solo le parti "input_config" e "player_input_assignments"
//             (il resto del file sono le TUE impostazioni e resta byte per byte), più le
//             configurazioni per gioco in games\<id>\Config.json
//    Cemu     la cartella controllerProfiles: controller0..7.xml
//    Dolphin  la cartella Config: GCPadNew.ini e WiimoteNew.ini
//    RPCS3    la cartella input_configs: active_input_configurations.yml e global\*.yml
//  Nei nomi dei file messi da parte le sottocartelle si scrivono con "/" (es. "global/Default.yml"),
//  su tutti i sistemi.
// =====================================================================================

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SteamAll.Launcher;

public static class ControllerSwitch
{
    public const string DataFolder = "SteamAll";   // <Gioco>\SteamAll
    public const string Steam = "Steam";
    public const string Own = "Own";

    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

    // ---------------------------------------------------------------------------------
    //  Da dove parte il gioco?
    //  Steam, quando avvia un gioco, gli passa alcune variabili d'ambiente (SteamGameId...); se per
    //  quel gioco Steam Input è attivo ne aggiunge altre (SteamVirtualGamepadInfo...). Con
    //  entrambe: siamo partiti da Steam e l'emulatore vedrà il controller virtuale di Steam.
    // ---------------------------------------------------------------------------------

    public static string DetectMode(Func<string, string?> getEnvironment, out string details)
    {
        List<string> steam = Present(getEnvironment, "SteamGameId", "SteamOverlayGameId", "SteamClientLaunch", "SteamAppId");
        List<string> input = Present(getEnvironment, "SDL_GAMECONTROLLER_ALLOW_STEAM_VIRTUAL_GAMEPAD", "SteamVirtualGamepadInfo", "SDL_GAMECONTROLLER_IGNORE_DEVICES");
        details = "Steam variables: " + List(steam) + "; Steam Input variables: " + List(input);
        return steam.Count > 0 && input.Count > 0 ? Steam : Own;
    }

    private static List<string> Present(Func<string, string?> getEnvironment, params string[] names)
    {
        var result = new List<string>();
        foreach (string n in names)
        {
            string? v = getEnvironment(n);
            if (!string.IsNullOrEmpty(v) && v != "0") result.Add(n);
        }
        return result;
    }

    private static string List(List<string> items) => items.Count > 0 ? string.Join(", ", items.ToArray()) : "none";

    // ---------------------------------------------------------------------------------
    //  Il cambio, chiamato dal launcher prima di avviare l'emulatore. Non blocca mai il gioco:
    //  se qualcosa non va, restituisce il motivo e il gioco parte con la configurazione che c'è.
    // ---------------------------------------------------------------------------------

    public static string Prepare(string configDir, string dataDir, string emulator, string mode)
    {
        string? reason = WhyNotManaged(configDir, emulator);
        if (reason != null) return "not managed: " + reason;

        string data = dataDir;
        Dictionary<string, string> state = ReadState(data);
        string? active;
        state.TryGetValue("Active", out active);
        Dictionary<string, byte[]> current = ReadActive(configDir, emulator);
        // Quale configurazione è in uso adesso? Se punta al controller virtuale di Steam è quella
        // "Steam" (lo si riconosce dalla sua identità); altrimenti quella che dice state.ini.
        string inUse = HasSteamSignature(current) ? Steam : (active == Steam ? Steam : Own);

        string result;
        if (inUse == mode)
        {
            result = mode + " setup already in use";
        }
        else
        {
            Dictionary<string, byte[]> wanted = ReadSlot(Path.Combine(data, mode));
            if (wanted.Count == 0)
            {
                // Per "Own" vuol dire che non l'hai ancora fatta: resta quella che c'è, e la prossima
                // volta che parti da Steam verrà messa da parte come tua.
                result = mode + " setup not ready yet: the current one stays";
                mode = inUse;
            }
            else
            {
                if (current.Count > 0) WriteSlot(Path.Combine(data, inUse), current);   // mettiamo da parte quella in uso
                WriteActive(configDir, emulator, wanted);
                result = "put aside the " + inUse + " setup, now using the " + mode + " setup";
            }
        }
        state["Active"] = mode;
        WriteState(data, state);
        return result;
    }

    // ---------------------------------------------------------------------------------
    //  Le regole di ogni emulatore
    // ---------------------------------------------------------------------------------

    private sealed class Spec
    {
        public Regex Files = new Regex("$^");   // i file che contano, relativi a configDir (al massimo una sottocartella)
        public bool DropMissing;                // i file che la configurazione non ha vanno tolti (i giocatori di Cemu)
    }

    private static Spec? SpecFor(string emulator)
    {
        switch (emulator)
        {
            case "Cemu":
                return new Spec { Files = new Regex(@"^controller[0-7]\.xml$", RegexOptions.IgnoreCase), DropMissing = true };
            case "Dolphin":
                return new Spec { Files = new Regex(@"^(GCPadNew|WiimoteNew)\.ini$", RegexOptions.IgnoreCase) };
            case "RPCS3":
                return new Spec { Files = new Regex(@"^(active_input_configurations\.yml|global/[^/]+\.yml)$", RegexOptions.IgnoreCase) };
        }
        return null;
    }

    /// <summary>
    /// Perché il controller di questo emulatore non si può gestire, o null se si può.
    /// configDir = la cartella della sua configurazione ("" se SteamAll non l'ha trovata).
    /// </summary>
    public static string? WhyNotManaged(string configDir, string emulator)
    {
        if (emulator == "Ryujinx")
        {
            if (configDir.Length == 0) return "Ryujinx's settings folder was not found";
            string config = Path.Combine(configDir, "Config.json");
            if (!File.Exists(config)) return "no Config.json yet: start the emulator once";
            int s, e;
            if (!Json.FindValue(Text(File.ReadAllBytes(config)), "input_config", out s, out e)) return "Config.json has no input_config";
            return null;
        }
        if (SpecFor(emulator) == null) return "controllers are not managed for this emulator";
        if (configDir.Length == 0) return emulator + "'s settings folder was not found";
        return null;
    }

    // Ryujinx: il Config.json comune e quelli dei singoli giochi (games\<id>\Config.json).
    private static List<string> RyujinxConfigs(string configDir)
    {
        var result = new List<string> { Path.Combine(configDir, "Config.json") };
        string games = Path.Combine(configDir, "games");
        if (Directory.Exists(games))
            foreach (string d in Directory.GetDirectories(games))
            {
                string f = Path.Combine(d, "Config.json");
                if (File.Exists(f)) result.Add(f);
            }
        return result;
    }

    private static readonly Regex OwnInput = new Regex("\"use_input_global_config\"\\s*:\\s*false");

    // La configurazione che Ryujinx usa davvero: quella del gioco se ha controller suoi, altrimenti la comune.
    private static string RyujinxEffectiveConfig(string configDir)
    {
        List<string> all = RyujinxConfigs(configDir);
        for (int i = 1; i < all.Count; i++)
        {
            string t = Text(File.ReadAllBytes(all[i]));
            int s, e;
            if (OwnInput.IsMatch(t) && Json.FindValue(t, "input_config", out s, out e)) return all[i];
        }
        return all[0];
    }

    /// <summary>La configurazione in uso adesso nell'emulatore: nome del file -> contenuto.</summary>
    public static Dictionary<string, byte[]> ReadActive(string configDir, string emulator)
    {
        var items = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (WhyNotManaged(configDir, emulator) != null) return items;
        if (emulator == "Ryujinx")
        {
            string text = Text(File.ReadAllBytes(RyujinxEffectiveConfig(configDir)));
            int s, e;
            Json.FindValue(text, "input_config", out s, out e);
            items["input_config.json"] = Utf8.GetBytes(text.Substring(s, e - s));
            return items;
        }
        Spec spec = SpecFor(emulator)!;
        foreach (string rel in ManagedFiles(configDir, spec)) items[rel] = File.ReadAllBytes(Path.Combine(configDir, rel));
        return items;
    }

    /// <summary>Mette una configurazione in uso nell'emulatore.</summary>
    public static void WriteActive(string configDir, string emulator, Dictionary<string, byte[]> items)
    {
        string? reason = WhyNotManaged(configDir, emulator);
        if (reason != null) throw new InvalidOperationException(reason);
        if (emulator == "Ryujinx")
        {
            byte[]? array;
            if (!items.TryGetValue("input_config.json", out array) || array == null) return;
            foreach (string config in RyujinxConfigs(configDir)) WriteRyujinxInput(config, Text(array));
            return;
        }
        Spec spec = SpecFor(emulator)!;
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, byte[]> item in items)
        {
            string rel = item.Key.Replace('\\', '/');
            written.Add(rel);
            WriteFile(Path.Combine(configDir, rel), item.Value);
        }
        if (spec.DropMissing)
            foreach (string rel in ManagedFiles(configDir, spec))
                if (!written.Contains(rel)) File.Delete(Path.Combine(configDir, rel));   // un giocatore in meno: quel file di Cemu non c'era
    }

    // Ryujinx: si cambiano solo input_config e player_input_assignments (il dispositivo di ogni
    // giocatore, che deve corrispondere); tutto il resto del file resta identico.
    private static void WriteRyujinxInput(string config, string array)
    {
        byte[] original = File.ReadAllBytes(config);
        string text = Text(original);
        int s, e;
        if (!Json.FindValue(text, "input_config", out s, out e)) return;
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        string value = array.Trim().Replace("\r\n", "\n").Replace("\n", newline);
        text = text.Substring(0, s) + value + text.Substring(e);
        if (Json.FindValue(text, "player_input_assignments", out s, out e) &&
            !Regex.IsMatch(text.Substring(s, e - s), "\"enable_dynamic_input_swap\"\\s*:\\s*true"))
            text = text.Substring(0, s) + Json.RyujinxAssignments(value, newline) + text.Substring(e);
        byte[] data = Utf8.GetBytes(text);
        if (HasBom(original))
        {
            var withBom = new byte[data.Length + 3];
            withBom[0] = 0xEF; withBom[1] = 0xBB; withBom[2] = 0xBF;
            Buffer.BlockCopy(data, 0, withBom, 3, data.Length);
            data = withBom;
        }
        if (Text(data) != Text(original)) WriteFile(config, data);
    }

    private static List<string> ManagedFiles(string dir, Spec spec)
    {
        var result = new List<string>();
        if (!Directory.Exists(dir)) return result;
        foreach (string f in Directory.GetFiles(dir))
            if (spec.Files.IsMatch(Path.GetFileName(f))) result.Add(Path.GetFileName(f));
        foreach (string d in Directory.GetDirectories(dir))
            foreach (string f in Directory.GetFiles(d))
            {
                string rel = Path.GetFileName(d) + "/" + Path.GetFileName(f);   // "/" su tutti i sistemi
                if (spec.Files.IsMatch(rel)) result.Add(rel);
            }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    // ---------------------------------------------------------------------------------
    //  Le configurazioni messe da parte (una cartella per configurazione) e state.ini
    // ---------------------------------------------------------------------------------

    public static Dictionary<string, byte[]> ReadSlot(string dir)
    {
        var items = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(dir)) return items;
        string root = dir.TrimEnd('\\', '/');
        foreach (string f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            items[Key(root, f)] = File.ReadAllBytes(f);
        return items;
    }

    public static void WriteSlot(string dir, Dictionary<string, byte[]> items)
    {
        string root = dir.TrimEnd('\\', '/');
        Directory.CreateDirectory(root);
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string k in items.Keys) wanted.Add(k.Replace('\\', '/'));
        foreach (string f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            if (!wanted.Contains(Key(root, f))) File.Delete(f);   // file nostri, in una cartella nostra
        foreach (KeyValuePair<string, byte[]> item in items) WriteFile(Path.Combine(root, item.Key.Replace('\\', '/')), item.Value);
    }

    // Il nome di un file messo da parte: relativo alla cartella, con "/" tra le sottocartelle.
    private static string Key(string root, string file) => file.Substring(root.Length + 1).Replace('\\', '/');

    public static Dictionary<string, string> ReadState(string dataDir)
    {
        var state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string f = Path.Combine(dataDir, "state.ini");
        if (!File.Exists(f)) return state;
        foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
        {
            int i = line.IndexOf('=');
            if (i > 0 && !line.StartsWith(";")) state[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
        }
        return state;
    }

    public static void WriteState(string dataDir, Dictionary<string, string> state)
    {
        var keys = new List<string>(state.Keys);
        keys.Sort(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.Append("; SteamAll - state of this game, written by SteamAll and by the game launcher.\r\n");
        sb.Append("; Active = controller setup in use in the emulator: Steam (started from Steam) or Own (yours).\r\n");
        foreach (string k in keys) sb.Append(k).Append('=').Append(state[k]).Append("\r\n");
        WriteFile(Path.Combine(dataDir, "state.ini"), Utf8.GetBytes(sb.ToString()));
    }

    // Il controller virtuale di Steam si riconosce dalla sua identità (vendor 28DE, prodotto 11FF).
    private static readonly Regex SteamSignature = new Regex(@"28de-0000-ff11|de280000ff11|Steam Virtual Gamepad", RegexOptions.IgnoreCase);

    public static bool HasSteamSignature(Dictionary<string, byte[]> items)
    {
        foreach (byte[] b in items.Values)
            if (SteamSignature.IsMatch(Text(b))) return true;
        return false;
    }

    // Scrive un file in modo sicuro: prima accanto, poi al posto di quello vecchio.
    public static void WriteFile(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".steamall.tmp";
        File.WriteAllBytes(temp, data);
        if (File.Exists(path)) File.Replace(temp, path, null);
        else File.Move(temp, path);
    }

    public static string Text(byte[] b) => HasBom(b) ? Utf8.GetString(b, 3, b.Length - 3) : Utf8.GetString(b);

    private static bool HasBom(byte[] b) => b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;

    // ---------------------------------------------------------------------------------
    //  Un mini-lettore di JSON "a mano": trova DOVE sta un valore nel testo, così possiamo
    //  sostituire solo quel pezzo e lasciare il resto del file esattamente com'era (spazi,
    //  a capo, ordine). Una libreria JSON riscriverebbe tutto il file a modo suo.
    // ---------------------------------------------------------------------------------
    internal static class Json
    {
        /// <summary>Dove sta il valore di una chiave del primo livello: json.Substring(start, end - start).</summary>
        public static bool FindValue(string json, string key, out int start, out int end)
        {
            start = -1;
            end = -1;
            int depth = 0, i = 0;
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '"')
                {
                    int s = i;
                    i = StringEnd(json, i);
                    if (depth == 1)
                    {
                        int j = SkipSpaces(json, i);
                        if (j < json.Length && json[j] == ':' && json.Substring(s + 1, i - s - 2) == key)
                        {
                            start = SkipSpaces(json, j + 1);
                            end = ValueEnd(json, start);
                            return end > start;
                        }
                    }
                    continue;
                }
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
                i++;
            }
            return false;
        }

        // Le assegnazioni dei giocatori di Ryujinx, ricavate da input_config: un dispositivo per giocatore.
        public static string RyujinxAssignments(string array, string newline)
        {
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (int[] element in ArrayElements(array))
            {
                string t = array.Substring(element[0], element[1] - element[0]);
                string? player = Field(t, "player_index");
                if (player == null) continue;
                string type = (Field(t, "backend") ?? "").StartsWith("Gamepad") ? "Controller" : "Keyboard";
                sb.Append(first ? "" : ",").Append(newline)
                  .Append("    {").Append(newline)
                  .Append("      \"player_index\": \"").Append(player).Append("\",").Append(newline)
                  .Append("      \"enable_dynamic_input_swap\": false,").Append(newline)
                  .Append("      \"devices\": [").Append(newline)
                  .Append("        {").Append(newline)
                  .Append("          \"type\": \"").Append(type).Append("\",").Append(newline)
                  .Append("          \"id\": \"").Append(Field(t, "id") ?? "").Append("\",").Append(newline)
                  .Append("          \"profile_name\": null").Append(newline)
                  .Append("        }").Append(newline)
                  .Append("      ]").Append(newline)
                  .Append("    }");
                first = false;
            }
            return sb.Append(newline).Append("  ]").ToString();
        }

        private static List<int[]> ArrayElements(string array)   // [inizio, fine) di ogni elemento
        {
            var result = new List<int[]>();
            int i = SkipSpaces(array, 0);
            if (i >= array.Length || array[i] != '[') return result;
            i++;
            while (true)
            {
                i = SkipSpaces(array, i);
                if (i >= array.Length || array[i] == ']') break;
                if (array[i] == ',') { i++; continue; }
                int end = ValueEnd(array, i);
                if (end <= i) break;
                result.Add(new[] { i, end });
                i = end;
            }
            return result;
        }

        private static string? Field(string obj, string key)
        {
            Match m = Regex.Match(obj, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static int SkipSpaces(string s, int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            return i;
        }

        private static int StringEnd(string s, int i)   // i = la virgoletta di apertura; dà la posizione dopo quella di chiusura
        {
            for (i++; i < s.Length; i++)
            {
                if (s[i] == '\\') { i++; continue; }
                if (s[i] == '"') return i + 1;
            }
            return s.Length;
        }

        private static int ValueEnd(string s, int i)
        {
            if (i >= s.Length) return i;
            if (s[i] == '"') return StringEnd(s, i);
            if (s[i] == '{' || s[i] == '[')
            {
                int depth = 0;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == '"') { i = StringEnd(s, i); continue; }
                    if (c == '{' || c == '[') depth++;
                    else if ((c == '}' || c == ']') && --depth == 0) return i + 1;
                    i++;
                }
                return s.Length;
            }
            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']' && !char.IsWhiteSpace(s[i])) i++;
            return i;
        }
    }
}
