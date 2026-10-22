// =====================================================================================
//  SteamInputSettings.cs
//  Steam Input è acceso per il tipo di controller che hai collegato? Steam lo scrive, per ogni
//  utente, in userdata\<id>\config\localconfig.vdf (Steam > Impostazioni > Controller):
//      "SteamController_XBoxSupport"            "1"   controller Xbox (e quelli XInput, come 8BitDo)
//      "SteamController_PSSupport"              "2"   PlayStation
//      "SteamController_SwitchSupport"          "1"   Nintendo (Switch Pro, Joy-Con)
//      "SteamController_GenericGamepadSupport"  "0"   tutti gli altri
//  0 = spento, altri valori = acceso; se la voce manca, Steam usa il suo valore predefinito.
//  Con Steam Input acceso, i giochi avviati da Steam (emulati compresi) vedono il controller
//  virtuale di Steam: è quello per cui SteamAll prepara le configurazioni degli emulatori.
//  Solo lettura.
// =====================================================================================

using System.Text.RegularExpressions;

namespace SteamAll.Core.Controllers;

/// <summary>La famiglia di un controller, per le impostazioni di Steam Input.</summary>
public enum ControllerFamily { Xbox, PlayStation, Nintendo, Generic }

public static class SteamInputSettings
{
    /// <summary>La famiglia dal produttore del controller (il "vendor id" USB).</summary>
    public static ControllerFamily FamilyOf(int vendorId) => vendorId switch
    {
        0x045E => ControllerFamily.Xbox,          // Microsoft
        0x054C => ControllerFamily.PlayStation,   // Sony
        0x057E => ControllerFamily.Nintendo,      // Nintendo
        _ => ControllerFamily.Generic,
    };

    private static string Key(ControllerFamily family) => family switch
    {
        ControllerFamily.Xbox => "SteamController_XBoxSupport",
        ControllerFamily.PlayStation => "SteamController_PSSupport",
        ControllerFamily.Nintendo => "SteamController_SwitchSupport",
        _ => "SteamController_GenericGamepadSupport",
    };

    /// <summary>
    /// Steam Input è acceso per questa famiglia? null = la voce non c'è (vale il predefinito di Steam).
    /// "localConfigText" = il testo di localconfig.vdf.
    /// </summary>
    public static bool? IsEnabled(string localConfigText, ControllerFamily family)
    {
        Match m = Regex.Match(localConfigText, "\"" + Key(family) + "\"\\s+\"(\\d+)\"");
        return m.Success ? m.Groups[1].Value != "0" : null;
    }

    /// <summary>Come sopra, leggendo localconfig.vdf dalla cartella config dell'utente di Steam ("" se manca).</summary>
    public static bool? IsEnabledFor(string userConfigFolder, ControllerFamily family)
    {
        string file = Path.Combine(userConfigFolder, "localconfig.vdf");
        return File.Exists(file) ? IsEnabled(File.ReadAllText(file), family) : null;
    }
}
