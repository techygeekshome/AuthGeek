using AuthGeek.Core.Services;

namespace AuthGeek.Core.Models;

/// <summary>
/// One two-factor account: the shared secret and everything needed to turn it into the right code.
///
/// The defaults are the ones nearly every service uses. They are still stored per account rather
/// than assumed, because the handful of services that differ are exactly the ones somebody would
/// be locked out of if the defaults were baked in.
/// </summary>
public sealed class Account
{
    /// <summary>Stable across renames, so a rename is not a delete and an add.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>The service. "GitHub", "Nationwide".</summary>
    public required string Issuer { get; set; }

    /// <summary>Which account at that service. Usually an email address.</summary>
    public string Label { get; set; } = "";

    /// <summary>Base32, exactly as the service gave it.</summary>
    public required string Secret { get; set; }

    public OtpKind Kind { get; set; } = OtpKind.Totp;
    public OtpAlgorithm Algorithm { get; set; } = OtpAlgorithm.Sha1;
    public int Digits { get; set; } = 6;
    public int Period { get; set; } = 30;

    /// <summary>Counter-based accounts only. Goes up every time a code is asked for.</summary>
    public long Counter { get; set; }

    public DateTimeOffset Added { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>What to show when there is no issuer, so a row is never blank.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Issuer)
        ? (string.IsNullOrWhiteSpace(Label) ? "Unnamed" : Label)
        : Issuer;

    public Account Copy() => new()
    {
        Id = Id,
        Issuer = Issuer,
        Label = Label,
        Secret = Secret,
        Kind = Kind,
        Algorithm = Algorithm,
        Digits = Digits,
        Period = Period,
        Counter = Counter,
        Added = Added
    };
}

public enum OtpKind
{
    /// <summary>Time based. What almost everything uses.</summary>
    Totp,

    /// <summary>Counter based. Rare, but a few banks still use it.</summary>
    Hotp
}

/// <summary>What came out of an import, including what could not be read and why.</summary>
/// <param name="Accounts">Everything that was understood.</param>
/// <param name="Problems">One line per thing that was not, so nothing fails silently.</param>
public sealed record ImportResult(IReadOnlyList<Account> Accounts, IReadOnlyList<string> Problems)
{
    public static ImportResult Empty { get; } = new(Array.Empty<Account>(), Array.Empty<string>());
    public int Count => Accounts.Count;
}
