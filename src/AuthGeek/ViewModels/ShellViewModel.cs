using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AuthGeek.Core.Models;
using AuthGeek.Core.Services;
using Avalonia.Threading;

namespace AuthGeek.ViewModels;

/// <summary>
/// The whole application's state.
///
/// One thing here is worth calling out. The master password is held in memory for as long as the
/// vault is unlocked, because every save needs it and asking for it on every change would make
/// the app unusable. It is dropped the moment the vault locks, and the vault locks on a timer as
/// well as on request, because a two-factor app left unlocked on a desk is a two-factor app that
/// is not doing its job.
/// </summary>
public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private string? _password;
    private DispatcherTimer? _tick;
    private DateTimeOffset _lastUsed = DateTimeOffset.UtcNow;

    public ShellViewModel()
    {
        ShowCodes = new RelayCommand(() => Page = "Codes");
        ShowAdd = new RelayCommand(() => Page = "Add");
        ShowBackup = new RelayCommand(() => Page = "Backup");
        ShowSettings = new RelayCommand(() => Page = "Settings");

        Lock = new RelayCommand(LockNow);

        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => OnSecond();
        _tick.Start();
    }

    // ---------------------------------------------------------------- locked or not

    private bool _isUnlocked;
    public bool IsUnlocked
    {
        get => _isUnlocked;
        private set
        {
            if (!SetField(ref _isUnlocked, value)) return;
            OnPropertyChanged(nameof(IsLocked));
            OnPropertyChanged(nameof(StatusLine));
        }
    }

    public bool IsLocked => !IsUnlocked;

    /// <summary>True the first time the app runs, when there is no vault to unlock yet.</summary>
    public bool IsFirstRun => !Vault.Exists();

    private string _unlockMessage = "";
    public string UnlockMessage { get => _unlockMessage; set => SetField(ref _unlockMessage, value); }

    private bool _unlockFailed;
    public bool UnlockFailed { get => _unlockFailed; private set => SetField(ref _unlockFailed, value); }

    /// <summary>
    /// Opens the vault, or creates one on a first run. The two are deliberately the same button:
    /// there is nothing to decide on a first run except what the password is going to be.
    /// </summary>
    public bool Unlock(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            UnlockMessage = "A vault with no password is not protected by anything.";
            UnlockFailed = true;
            return false;
        }

        try
        {
            List<Account> loaded;

            if (Vault.Exists())
            {
                loaded = Vault.Open(password).Select(a => a.Copy()).ToList();
            }
            else
            {
                if (password.Length < 8)
                {
                    UnlockMessage = "Use at least eight characters. This is the only thing standing " +
                                    "between somebody with your computer and every code you have.";
                    UnlockFailed = true;
                    return false;
                }

                loaded = new List<Account>();
                Vault.Save(loaded, password);
            }

            _password = password;

            Accounts.Clear();
            foreach (var a in loaded.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase))
                Accounts.Add(new AccountViewModel(a, this));

            IsUnlocked = true;
            UnlockFailed = false;
            UnlockMessage = "";
            Touch();
            RefreshList();
            return true;
        }
        catch (WrongPasswordException ex)
        {
            UnlockMessage = ex.Message;
            UnlockFailed = true;
            return false;
        }
        catch (VaultDamagedException ex)
        {
            UnlockMessage = ex.Message;
            UnlockFailed = true;
            return false;
        }
        catch (Exception ex)
        {
            Log.Write("Unlock: " + ex);
            UnlockMessage = "The vault could not be opened: " + ex.Message;
            UnlockFailed = true;
            return false;
        }
    }

    public ICommand Lock { get; }

    public void LockNow()
    {
        _password = null;
        Accounts.Clear();
        Visible.Clear();
        IsUnlocked = false;
        Page = "Codes";
        Search = "";
        UnlockMessage = "";
        UnlockFailed = false;
        OnPropertyChanged(nameof(IsFirstRun));
    }

    /// <summary>Called whenever the person does something, so the auto-lock clock starts again.</summary>
    public void Touch() => _lastUsed = DateTimeOffset.UtcNow;

    // ---------------------------------------------------------------- the clock

    /// <summary>
    /// Once a second: recompute every visible code, move the countdown, and lock if the app has
    /// been sitting untouched for long enough.
    /// </summary>
    private void OnSecond()
    {
        if (!IsUnlocked) return;

        if (AutoLockMinutes > 0 &&
            DateTimeOffset.UtcNow - _lastUsed > TimeSpan.FromMinutes(AutoLockMinutes))
        {
            LockNow();
            UnlockMessage = $"Locked after {AutoLockMinutes} minutes without being used.";
            return;
        }

        foreach (var a in Visible) a.Tick();
    }

    private int _autoLockMinutes = 5;
    /// <summary>Minutes of doing nothing before the vault locks itself. Zero turns it off.</summary>
    public int AutoLockMinutes
    {
        get => _autoLockMinutes;
        set { if (SetField(ref _autoLockMinutes, value)) OnPropertyChanged(nameof(AutoLockText)); }
    }

    public ObservableCollection<LockOption> LockOptions { get; } = new(LockOption.All);

    private LockOption _selectedLockOption = LockOption.All[1];
    public LockOption SelectedLockOption
    {
        get => _selectedLockOption;
        set { if (SetField(ref _selectedLockOption, value)) AutoLockMinutes = value.Minutes; }
    }

    public string AutoLockText => AutoLockMinutes == 0
        ? "AuthGeek will stay unlocked until you lock it or close it."
        : $"AuthGeek locks itself after {AutoLockMinutes} minutes without being used.";

    // ---------------------------------------------------------------- navigation

    private string _page = "Codes";
    public string Page
    {
        get => _page;
        set
        {
            if (!SetField(ref _page, value)) return;
            Touch();
            OnPropertyChanged(nameof(IsCodes));
            OnPropertyChanged(nameof(IsAdd));
            OnPropertyChanged(nameof(IsBackup));
            OnPropertyChanged(nameof(IsSettings));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(StatusLine));
        }
    }

    public bool IsCodes => Page == "Codes";
    public bool IsAdd => Page == "Add";
    public bool IsBackup => Page == "Backup";
    public bool IsSettings => Page == "Settings";

    public ICommand ShowCodes { get; }
    public ICommand ShowAdd { get; }
    public ICommand ShowBackup { get; }
    public ICommand ShowSettings { get; }

    // ---------------------------------------------------------------- chrome

    public string BrandName => "AuthGeek";
    public string BrandBy => "by TechyGeeksHome";
    public string VersionText => TechyGeeksHome.Common.AppInfo.CurrentVersionText;
    public string VaultPath => Vault.DefaultPath;

    public string PageTitle => Page switch
    {
        "Add" => "Add an account",
        "Backup" => "Backup",
        "Settings" => "Settings",
        _ => "Codes"
    };

    public string StatusLine => Page switch
    {
        "Add" => "Paste a link, read a QR code from a picture, or type it in. Nothing leaves this machine.",
        "Backup" => "A two-factor secret cannot be recovered from anywhere. Keep a backup.",
        "Settings" => "What AuthGeek will and will not do, in plain words.",
        _ => Accounts.Count == 0
            ? "No accounts yet. Add one, or restore a backup."
            : $"{Accounts.Count} account{(Accounts.Count == 1 ? "" : "s")}"
              + (Visible.Count != Accounts.Count ? $" · {Visible.Count} shown" : "")
    };

    // ---------------------------------------------------------------- the accounts

    private readonly List<AccountViewModel> _all = new();
    public ObservableCollection<AccountViewModel> Accounts { get; } = new();
    public ObservableCollection<AccountViewModel> Visible { get; } = new();

    private string _search = "";
    public string Search
    {
        get => _search;
        set { if (SetField(ref _search, value)) RefreshList(); }
    }

    public bool HasAccounts => Accounts.Count > 0;

    private void RefreshList()
    {
        Visible.Clear();

        var term = Search.Trim();
        foreach (var a in Accounts)
        {
            if (term.Length > 0 &&
                a.Account.DisplayName.Contains(term, StringComparison.CurrentCultureIgnoreCase) == false &&
                a.Account.Label.Contains(term, StringComparison.CurrentCultureIgnoreCase) == false)
                continue;

            a.Tick();
            Visible.Add(a);
        }

        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(StatusLine));
        OnPropertyChanged(nameof(NothingMatches));
    }

    public bool NothingMatches => Accounts.Count > 0 && Visible.Count == 0;

    // ---------------------------------------------------------------- changing things

    /// <summary>
    /// Writes the vault. Every change goes through here, so there is one place that saves and one
    /// place that can report a save going wrong.
    /// </summary>
    public bool Save(out string problem)
    {
        problem = "";

        if (_password is null)
        {
            problem = "The vault is locked.";
            return false;
        }

        try
        {
            Vault.Save(Accounts.Select(a => a.Account), _password);
            Touch();
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("Save: " + ex);
            problem = "The vault could not be saved: " + ex.Message;
            return false;
        }
    }

    public IReadOnlyList<Account> Snapshot() => Accounts.Select(a => a.Account.Copy()).ToList();

    /// <summary>Adds accounts and saves. Returns what happened, in words, for the screen to show.</summary>
    public string AddAccounts(IReadOnlyList<Account> incoming)
    {
        if (incoming.Count == 0) return "There was nothing to add.";

        var merged = Backup.Merge(Snapshot(), incoming);

        Accounts.Clear();
        foreach (var a in merged.Accounts.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            Accounts.Add(new AccountViewModel(a, this));

        RefreshList();

        if (!Save(out var problem)) return problem;

        var said = new List<string>();
        if (merged.Added > 0) said.Add($"{merged.Added} added");
        if (merged.AlreadyThere > 0) said.Add($"{merged.AlreadyThere} already there");
        var summary = said.Count > 0 ? string.Join(", ", said) + "." : "Nothing changed.";

        return merged.Notes.Count > 0 ? summary + " " + string.Join(" ", merged.Notes) : summary;
    }

    public string Remove(AccountViewModel account)
    {
        Accounts.Remove(account);
        RefreshList();
        return Save(out var problem) ? $"{account.Account.DisplayName} removed." : problem;
    }

    public string ChangePassword(string current, string next)
    {
        if (next.Length < 8) return "Use at least eight characters.";

        try
        {
            Vault.ChangePassword(current, next);
            _password = next;
            return "The master password has been changed. The previous vault is still next to it as a .bak file, and that one still needs the old password.";
        }
        catch (WrongPasswordException)
        {
            return "That is not the current master password.";
        }
        catch (Exception ex)
        {
            Log.Write("ChangePassword: " + ex);
            return "The password could not be changed: " + ex.Message;
        }
    }

    public void Dispose()
    {
        _tick?.Stop();
        _tick = null;
        _password = null;
    }
}

/// <summary>How long to leave it before locking itself.</summary>
public sealed record LockOption(int Minutes, string Name)
{
    public override string ToString() => Name;

    public static readonly LockOption[] All =
    {
        new(1, "After 1 minute"),
        new(5, "After 5 minutes"),
        new(15, "After 15 minutes"),
        new(60, "After an hour"),
        new(0, "Never, until I lock it")
    };
}

/// <summary>Minimal INotifyPropertyChanged, matching the hand-rolled one in the other apps.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

/// <summary>A command with no parameter. Enough for this app; no need for a toolkit.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _run;
    private readonly Func<bool>? _can;

    public RelayCommand(Action run, Func<bool>? can = null) { _run = run; _can = can; }

    public bool CanExecute(object? parameter) => _can?.Invoke() ?? true;
    public void Execute(object? parameter) => _run();
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
