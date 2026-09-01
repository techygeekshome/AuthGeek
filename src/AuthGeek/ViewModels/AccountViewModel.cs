using System.Windows.Input;
using AuthGeek.Core.Models;
using AuthGeek.Core.Services;
using Avalonia.Media;

namespace AuthGeek.ViewModels;

/// <summary>
/// One row on the Codes screen: the current code, how long it has left, and the buttons that act
/// on it.
///
/// The code is recomputed every second rather than cached with a timer per account, because a
/// machine that has been asleep comes back with a stale code and no event to tell anyone. Working
/// it out from the clock every time means it is right the instant the window is looked at.
/// </summary>
public sealed class AccountViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public AccountViewModel(Account account, ShellViewModel shell)
    {
        Account = account;
        _shell = shell;

        NextCode = new RelayCommand(Advance);
        Tick();
    }

    public Account Account { get; }

    public string Title => Account.DisplayName;
    public string Subtitle => Account.Label;
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Account.Label)
                               && !string.Equals(Account.Label, Account.DisplayName, StringComparison.Ordinal);

    // ---------------------------------------------------------------- the code

    private string _code = "";
    /// <summary>The code, spaced in the middle, because that is how people read six digits aloud.</summary>
    public string Code { get => _code; private set => SetField(ref _code, value); }

    /// <summary>The code with no spacing. What actually goes on the clipboard.</summary>
    public string RawCode { get; private set; } = "";

    private int _secondsLeft;
    public int SecondsLeft
    {
        get => _secondsLeft;
        private set
        {
            if (!SetField(ref _secondsLeft, value)) return;
            OnPropertyChanged(nameof(Remaining));
            OnPropertyChanged(nameof(CountdownBrush));
            OnPropertyChanged(nameof(CountdownText));
        }
    }

    /// <summary>How much of the period is left, from 0 to 100, for the bar.</summary>
    public double Remaining => Account.Kind == OtpKind.Hotp
        ? 100
        : Math.Clamp(SecondsLeft / (double)Math.Max(1, Account.Period) * 100, 0, 100);

    public string CountdownText => Account.Kind == OtpKind.Hotp
        ? $"counter {Account.Counter}"
        : $"{SecondsLeft}s";

    /// <summary>Goes amber under ten seconds, so it is obvious when not to start typing it.</summary>
    public IBrush CountdownBrush => Account.Kind == OtpKind.Hotp
        ? Brush.Parse("#7C8699")
        : SecondsLeft <= 5 ? Brush.Parse("#E86A6A")
        : SecondsLeft <= 10 ? Brush.Parse("#E8B45A")
        : Brush.Parse("#7BA9F6");

    public bool IsCounterBased => Account.Kind == OtpKind.Hotp;
    public bool IsTimeBased => Account.Kind == OtpKind.Totp;

    private string _problem = "";
    /// <summary>Shown instead of a code when the secret cannot produce one.</summary>
    public string Problem { get => _problem; private set => SetField(ref _problem, value); }

    public bool HasProblem => Problem.Length > 0;

    public ICommand NextCode { get; }

    /// <summary>Recomputes the code and the countdown from the clock.</summary>
    public void Tick()
    {
        try
        {
            var secret = Base32.Decode(Account.Secret);
            var now = DateTimeOffset.UtcNow;

            RawCode = Account.Kind == OtpKind.Hotp
                ? Otp.Hotp(secret, Account.Counter, Account.Digits, Account.Algorithm)
                : Otp.Totp(secret, now, Account.Period, Account.Digits, Account.Algorithm);

            Code = Space(RawCode);
            SecondsLeft = Account.Kind == OtpKind.Hotp ? 0 : Otp.SecondsRemaining(now, Account.Period);

            Problem = "";
        }
        catch (FormatException ex)
        {
            // A secret that will not decode is a real state: it happens when somebody types one
            // in by hand. Say so on the row rather than crashing the list.
            RawCode = "";
            Code = "";
            Problem = ex.Message;
        }

        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(HasCode));
    }

    public bool HasCode => RawCode.Length > 0;

    /// <summary>
    /// Counter-based accounts move on only when asked. Advancing writes the vault immediately,
    /// because a counter that is remembered in the window and not on disk is a counter that comes
    /// back wrong after a crash, and a wrong counter means codes that no longer work.
    /// </summary>
    private void Advance()
    {
        if (Account.Kind != OtpKind.Hotp) return;

        Account.Counter++;
        Tick();
        OnPropertyChanged(nameof(CountdownText));
        _shell.Save(out _);
    }

    /// <summary>A single gap in the middle. "123 456" reads far faster than "123456".</summary>
    private static string Space(string code) => code.Length switch
    {
        6 => code[..3] + " " + code[3..],
        8 => code[..4] + " " + code[4..],
        _ => code
    };
}
