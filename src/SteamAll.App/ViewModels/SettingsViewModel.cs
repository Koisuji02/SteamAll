// =====================================================================================
//  SettingsViewModel.cs
//  La pagina "Settings": cosa aggiunge SteamAll (solo i giochi installati, o tutta la libreria con
//  il tasto Installa e il suo plugin), l'account di Steam, la chiave API di SteamGridDB e qualche
//  opzione. Ogni modifica si salva da sola, subito.
// =====================================================================================

using System.Collections.ObjectModel;
using SteamAll.Core.Settings;
using SteamAll.Core.Steam;

namespace SteamAll.ViewModels;

/// <summary>Una voce del menu degli account: un utente di Steam, oppure "automatico" (User = null).</summary>
public sealed record AccountOption(string Label, SteamUser? User)
{
    public override string ToString() => Label;
}

public sealed class SettingsViewModel : PageViewModel
{
    private readonly MainWindowViewModel shell;
    private AccountOption? selectedAccount;
    private string apiKey;

    public override Avalonia.Media.Geometry Icon => Icons.Settings;

    public SettingsViewModel(MainWindowViewModel shell)
        : base("Settings", "What SteamAll adds to Steam, your Steam account and the artwork.")
    {
        this.shell = shell;
        apiKey = shell.Settings.GetSteamGridDbKey(shell.Platform);   // in memoria soltanto
    }

    // --- Cosa aggiunge SteamAll -----------------------------------------------------------------
    // Due RadioButton legati a due proprietà bool: scegliere uno dei due imposta la modalità.

    public bool IsInstalledOnly
    {
        get => shell.Settings.Mode == AppSettings.ModeInstalled;
        set { if (value) shell.SetMode(AppSettings.ModeInstalled); }
    }

    public bool IsWholeLibrary
    {
        get => shell.Settings.Mode != AppSettings.ModeInstalled;
        set
        {
            if (!value || shell.Settings.Mode == AppSettings.ModeAll) return;
            shell.SetMode(AppSettings.ModeAll);
            if (!shell.PluginInstalled) shell.Welcome.OpenSetup();   // serve il plugin: i passi per averlo
        }
    }

    /// <summary>Dopo un cambio di modalità (anche dal benvenuto): ridisegna le due scelte.</summary>
    public void OnModeChanged()
    {
        OnPropertyChanged(nameof(IsInstalledOnly));
        OnPropertyChanged(nameof(IsWholeLibrary));
    }

    // Il plugin del tasto Installa: lo stato e i pulsanti stanno nella finestra principale.
    public MainWindowViewModel Shell => shell;

    // --- Account di Steam -------------------------------------------------------------------

    public ObservableCollection<AccountOption> Accounts { get; } = [];

    public AccountOption? SelectedAccount
    {
        get => selectedAccount;
        set
        {
            if (!SetField(ref selectedAccount, value) || value is null) return;
            string id = value.User?.Id ?? "";
            if (id == shell.Settings.SteamUserId) return;
            shell.Settings.SteamUserId = id;
            shell.SaveSettings();
            _ = shell.RefreshAsync();   // un altro account: un'altra libreria di Steam da leggere
        }
    }

    /// <summary>Riempie il menu degli account con gli utenti di Steam trovati.</summary>
    public void SetUsers(IReadOnlyList<SteamUser> users)
    {
        Accounts.Clear();
        Accounts.Add(new AccountOption("Automatic (last used)", null));
        foreach (SteamUser u in users) Accounts.Add(new AccountOption(u.ToString(), u));
        // Il campo, non la proprietà: scegliere la voce iniziale non deve salvare né rileggere.
        selectedAccount = Accounts.FirstOrDefault(a => a.User?.Id == shell.Settings.SteamUserId) ?? Accounts[0];
        OnPropertyChanged(nameof(SelectedAccount));
    }

    // --- SteamGridDB ----------------------------------------------------------------------

    /// <summary>La chiave API: nel file finisce solo cifrata (vedi AppSettings).</summary>
    public string ApiKey
    {
        get => apiKey;
        set
        {
            if (!SetField(ref apiKey, value)) return;
            shell.Settings.SetSteamGridDbKey(shell.Platform, value);
            shell.SaveSettings();
        }
    }

    public bool HeroHd
    {
        get => shell.Settings.HeroHd;
        set
        {
            // Quando la pagina si apre, la casella "rimanda" il suo valore: se non è cambiato, niente salvataggio.
            if (value == shell.Settings.HeroHd) return;
            shell.Settings.HeroHd = value;
            shell.SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool Cleanup
    {
        get => shell.Settings.Cleanup;
        set
        {
            if (value == shell.Settings.Cleanup) return;
            shell.Settings.Cleanup = value;
            shell.SaveSettings();
            OnPropertyChanged();
        }
    }
}
