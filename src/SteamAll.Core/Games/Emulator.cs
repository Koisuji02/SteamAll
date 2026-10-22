// =====================================================================================
//  Emulator.cs
//  Il catalogo degli emulatori che SteamAll conosce. Serve in due modi:
//    1. cartelle dei giochi "emulatore portable + gioco" (C:\Games\<Gioco>\Ryujinx.exe + games\...):
//       l'emulatore si riconosce dal nome del suo exe (Exe) e dai suoi file tipici (Hints);
//    2. emulatori installati nel sistema + cartelle di ROM (come Steam ROM Manager): ogni emulatore
//       prende solo i file del suo sistema (Extensions, più qualche controllo sul contenuto, vedi RomScanner).
//  Per aggiungere un emulatore basta una riga nell'elenco All.
// =====================================================================================

namespace SteamAll.Core.Games;

/// <summary>Un emulatore del catalogo.</summary>
/// <param name="Id">Identificativo breve e stabile, salvato nelle impostazioni ("dolphin").</param>
/// <param name="Name">Il nome da mostrare, con il sistema: "Dolphin (GameCube/Wii)".</param>
/// <param name="Exe">Il nome dell'exe su Windows: quello che deve avere nelle cartelle portable.</param>
/// <param name="ExeNames">Altri nomi del programma (Linux, macOS, varianti): per riconoscerlo quando lo scegli.</param>
/// <param name="Hints">File o cartelle tipici: se ci sono tutti ma l'exe no, l'exe è stato rinominato.</param>
/// <param name="Extensions">I file dei giochi del suo sistema (minuscolo, con il punto).</param>
/// <param name="Arguments">I parametri per avviare un gioco: {rom} diventa il percorso del file.</param>
/// <param name="ControllerKind">Il tipo di configurazione del controller gestita ("" = l'emulatore fa da sé).</param>
/// <param name="FolderMode">true se si riconosce anche nelle cartelle "emulatore portable + gioco".</param>
// Un record "posizionale": i parametri tra parentesi diventano proprietà, in quest'ordine.
public sealed record Emulator(string Id, string Name, string Exe, string[] ExeNames, string[] Hints, string[] Extensions,
                              string Arguments, string ControllerKind, bool FolderMode)
{
    // L'elenco, uno solo per tutto il programma. "new(...)" = new Emulator(...): il tipo si deduce.
    public static IReadOnlyList<Emulator> All { get; } =
    [
        new("ryujinx", "Ryujinx (Switch)", "Ryujinx.exe", ["Ryujinx", "Ryujinx.sh", "Ryujinx.AppImage"],
            ["libSkiaSharp.dll", "libsoundio.dll"], [".nsp", ".xci"], "\"{rom}\"", "Ryujinx", true),
        new("cemu", "Cemu (Wii U)", "Cemu.exe", ["Cemu", "Cemu.AppImage"],
            ["gameProfiles", "resources"], [".wua", ".wux", ".wud", ".wuhb", ".rpx"], "-g \"{rom}\"", "Cemu", true),
        new("dolphin", "Dolphin (GameCube/Wii)", "Dolphin.exe", ["dolphin-emu", "Dolphin"],
            ["DolphinTool.exe", "Sys"], [".rvz", ".iso", ".gcm", ".wbfs", ".ciso", ".gcz", ".wia", ".tgc", ".nfs", ".wad", ".m3u"],
            "-b -e \"{rom}\"", "Dolphin", true),
        new("rpcs3", "RPCS3 (PS3)", "rpcs3.exe", ["rpcs3", "rpcs3.AppImage", "RPCS3"],
            ["dev_flash", "GuiConfigs"], [".bin"], "--no-gui \"{rom}\"", "RPCS3", true),   // .bin: solo gli EBOOT.BIN dei giochi (RomScanner)
        new("pcsx2", "PCSX2 (PS2)", "pcsx2-qt.exe", ["pcsx2-qtx64.exe", "pcsx2-qtx64-avx2.exe", "pcsx2-qt", "PCSX2", "pcsx2.AppImage"],
            [], [".iso", ".chd", ".cso", ".zso", ".gz", ".m3u"], "-batch -fullscreen \"{rom}\"", "", false),
        new("duckstation", "DuckStation (PS1)", "duckstation-qt-x64-ReleaseLTCG.exe", ["duckstation-qt", "DuckStation", "DuckStation.AppImage", "duckstation-nogui"],
            [], [".cue", ".chd", ".pbp", ".ecm", ".mds", ".m3u"], "-batch -fullscreen \"{rom}\"", "", false),
        new("ppsspp", "PPSSPP (PSP)", "PPSSPPWindows64.exe", ["PPSSPPWindows.exe", "PPSSPPSDL", "ppsspp", "PPSSPP"],
            [], [".iso", ".cso", ".pbp", ".chd"], "--fullscreen \"{rom}\"", "", false),
        new("melonds", "melonDS (DS)", "melonDS.exe", ["melonDS", "melonDS.AppImage"],
            [], [".nds", ".dsi"], "\"{rom}\"", "", false),
        new("azahar", "Azahar (3DS)", "azahar.exe", ["azahar", "Azahar", "citra-qt.exe", "citra-qt", "lime3ds-gui.exe"],
            [], [".3ds", ".cci", ".cxi", ".3dsx", ".app"], "\"{rom}\"", "", false),
        new("mgba", "mGBA (Game Boy / GBA)", "mGBA.exe", ["mgba-qt", "mGBA"],
            [], [".gba", ".gb", ".gbc", ".sgb"], "-f \"{rom}\"", "", false),
        new("xenia", "Xenia (Xbox 360)", "xenia_canary.exe", ["xenia.exe"],
            [], [".xex", ".zar", ".iso"], "--fullscreen \"{rom}\"", "", false),
    ];

    /// <summary>L'emulatore con questo id, o null.</summary>
    public static Emulator? Find(string id) => All.FirstOrDefault(e => e.Id == id);

    /// <summary>L'emulatore di questo programma, riconosciuto dal nome del file (maiuscole a parte), o null.</summary>
    public static Emulator? Recognize(string exePath)
    {
        string file = Path.GetFileName(exePath);
        return All.FirstOrDefault(e => string.Equals(e.Exe, file, StringComparison.OrdinalIgnoreCase)
                                    || e.ExeNames.Any(n => string.Equals(n, file, StringComparison.OrdinalIgnoreCase)));
    }
}
