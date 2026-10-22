// =====================================================================================
//  ControllerSetup.cs
//  Prepara i dati del controller di un emulatore (il cambio vero lo fa il launcher a ogni avvio,
//  con ControllerSwitch). Un "bersaglio" (ControllerTarget) è un emulatore da gestire:
//    - la copia portable nella cartella di un gioco (dati in <Gioco>\SteamAll);
//    - un emulatore installato, condiviso da tutti i suoi giochi (dati nella cartella del suo launcher).
//  Cosa prepara:
//    - la configurazione "Steam" (controller virtuale di Steam): se non c'è ancora, la prendiamo
//      dai profili inclusi in SteamAll, uno per emulatore. È il "plug & play".
//    - se il gioco era preparato da Setup_BoilR (<Gioco>\Setup_BoilR), riprendiamo le sue
//      configurazioni: "Steam" resta Steam, la "fisica" (o quella XInput) diventa la tua ("Own").
//      La cartella di Setup_BoilR resta com'è: è una copia di sicurezza.
//  Non tocca i file dell'emulatore: li cambierà il launcher al prossimo avvio del gioco.
// =====================================================================================

using System.Reflection;
using SteamAll.Core.Games;
using SteamAll.Core.Launchers;
using SteamAll.Core.Platform;
using SteamAll.Core.Settings;
using SteamAll.Launcher;

namespace SteamAll.Core.Controllers;

/// <summary>Un emulatore di cui gestire il controller.</summary>
/// <param name="Kind">Il tipo di configurazione (Emulator.ControllerKind): "Ryujinx", "Cemu"...; "" = nessuna.</param>
/// <param name="ConfigDir">La cartella della configurazione del controller dell'emulatore ("" se non si gestisce).</param>
/// <param name="DataDir">Dove stanno le configurazioni messe da parte (Steam, Own) e state.ini.</param>
/// <param name="LegacyDir">La cartella dei dati di Setup_BoilR da riprendere, o "".</param>
/// <param name="NotManagedReason">Perché non si può gestire (null se si può).</param>
public sealed record ControllerTarget(string Kind, string ConfigDir, string DataDir, string LegacyDir, string? NotManagedReason);

/// <summary>Lo stato del controller, per la scheda Controllers.</summary>
/// <param name="Active">La configurazione in uso adesso: "Steam", "Own" o "" (non si sa ancora).</param>
public sealed record ControllerStatus(string? NotManagedReason, bool SteamReady, bool OwnSaved, string Active);

public static class ControllerSetup
{
    private const string OldDataFolder = "Setup_BoilR";

    /// <summary>Il bersaglio di un gioco di una cartella (emulatore portable nella cartella del gioco).</summary>
    public static ControllerTarget ForFolderGame(Game game)
    {
        string kind = game.Emulator?.ControllerKind ?? "";
        string dir = game.InstallFolder;
        string? config = ConfigLocator.ForPortable(dir, kind, out string reason);
        return Target(kind, config, Path.Combine(dir, ControllerSwitch.DataFolder), Path.Combine(dir, OldDataFolder), reason);
    }

    /// <summary>Il bersaglio di un emulatore installato (vale per tutti i suoi giochi).</summary>
    public static ControllerTarget ForSetup(EmulatorSetup setup, OsKind os)
    {
        string kind = setup.Emulator?.ControllerKind ?? "";
        string? config = ConfigLocator.ForInstalled(setup.ExePath, kind, os, out string reason);
        return Target(kind, config, Path.Combine(LauncherInstaller.SetupHome(setup.Id), ControllerSwitch.DataFolder), "", reason);
    }

    private static ControllerTarget Target(string kind, string? configDir, string dataDir, string legacyDir, string reason) =>
        new(kind, configDir ?? "", dataDir, legacyDir,
            configDir is null ? reason : ControllerSwitch.WhyNotManaged(configDir, kind));

    /// <summary>Lo stato attuale, senza cambiare niente.</summary>
    public static ControllerStatus Status(ControllerTarget t)
    {
        bool steam = ControllerSwitch.ReadSlot(Path.Combine(t.DataDir, ControllerSwitch.Steam)).Count > 0;
        bool own = ControllerSwitch.ReadSlot(Path.Combine(t.DataDir, ControllerSwitch.Own)).Count > 0;
        string active = ControllerSwitch.ReadState(t.DataDir).GetValueOrDefault("Active", "");
        return new ControllerStatus(t.NotManagedReason, steam, own, active);
    }

    /// <summary>
    /// Prepara i dati del controller (se l'emulatore si può gestire). Restituisce cosa ha fatto,
    /// per il registro ("" se non c'era niente da fare).
    /// </summary>
    public static string Prepare(ControllerTarget t)
    {
        if (t.NotManagedReason is not null) return t.Kind.Length == 0 ? "" : "controllers not managed: " + t.NotManagedReason;

        var done = new List<string>();
        string steamSlot = Path.Combine(t.DataDir, ControllerSwitch.Steam);
        string ownSlot = Path.Combine(t.DataDir, ControllerSwitch.Own);

        // 1. Le configurazioni di Setup_BoilR, se ci sono e noi non ne abbiamo ancora.
        if (t.LegacyDir.Length > 0 && Directory.Exists(t.LegacyDir) && ControllerSwitch.ReadState(t.DataDir).Count == 0)
        {
            CopySlotIfMissing(Path.Combine(t.LegacyDir, "Steam"), steamSlot);
            // La "tua": prima quella fisica (Joy-Con...), altrimenti quella XInput.
            if (!CopySlotIfMissing(Path.Combine(t.LegacyDir, "Fisico"), ownSlot)) CopySlotIfMissing(Path.Combine(t.LegacyDir, "XInput"), ownSlot);
            string oldActive = ControllerSwitch.ReadState(t.LegacyDir).GetValueOrDefault("Attivo", "");
            ControllerSwitch.WriteState(t.DataDir, new Dictionary<string, string>
            {
                ["Active"] = oldActive == "Steam" ? ControllerSwitch.Steam : ControllerSwitch.Own,
            });
            done.Add("took over the controller setups of Setup_BoilR");
        }

        // 2. La configurazione "Steam" inclusa in SteamAll, se non ce n'è una.
        if (ControllerSwitch.ReadSlot(steamSlot).Count == 0)
        {
            Dictionary<string, byte[]> profile = BundledSteamProfile(t.Kind);
            if (profile.Count > 0)
            {
                ControllerSwitch.WriteSlot(steamSlot, profile);
                done.Add("added the Steam controller setup");
            }
        }
        return string.Join("; ", done);
    }

    private static bool CopySlotIfMissing(string from, string to)
    {
        Dictionary<string, byte[]> items = ControllerSwitch.ReadSlot(from);
        if (items.Count == 0 || ControllerSwitch.ReadSlot(to).Count > 0) return false;
        ControllerSwitch.WriteSlot(to, items);
        return true;
    }

    /// <summary>
    /// Il profilo "Steam" incluso per un emulatore (nome del file -> contenuto), dalle risorse
    /// incorporate "SteamProfiles\&lt;emulatore&gt;\...". I nomi delle sottocartelle con "/".
    /// </summary>
    public static Dictionary<string, byte[]> BundledSteamProfile(string kind)
    {
        var items = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (kind.Length == 0) return items;
        Assembly assembly = Assembly.GetExecutingAssembly();
        string prefix = $"SteamProfiles/{kind}/";
        foreach (string name in assembly.GetManifestResourceNames())
        {
            // Compilando su Windows i nomi delle risorse hanno "\", su Linux "/": li rendiamo uguali.
            string normalized = name.Replace('\\', '/');
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            using Stream stream = assembly.GetManifestResourceStream(name)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            items[normalized[prefix.Length..]] = memory.ToArray();
        }
        return items;
    }
}
