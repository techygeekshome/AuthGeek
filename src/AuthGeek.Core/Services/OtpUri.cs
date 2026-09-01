using System.Text;
using AuthGeek.Core.Models;

namespace AuthGeek.Core.Services;

/// <summary>
/// Reads and writes otpauth:// URIs, which is what is inside every two-factor QR code and what
/// every other authenticator can import.
///
/// The shape is otpauth://totp/Issuer:account@example.com?secret=...&amp;issuer=Issuer&amp;digits=6
///
/// Two things about it are habitually got wrong and are handled here. The issuer appears twice,
/// once in the path prefix and once as a parameter, and they can disagree; the parameter wins,
/// because that is what the spec says and what every other client does. And the label is URL
/// encoded, so an account with a colon or a space in it comes apart if it is split naively.
/// </summary>
public static class OtpUri
{
    public const string Scheme = "otpauth";

    public static Account Parse(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) throw new FormatException("There is nothing there to read.");

        var text = uri.Trim();
        if (!text.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("That is not an otpauth link. It should begin with otpauth://");

        Uri parsed;
        try
        {
            parsed = new Uri(text);
        }
        catch (UriFormatException)
        {
            throw new FormatException("That otpauth link is malformed.");
        }

        var kind = parsed.Host.Equals("hotp", StringComparison.OrdinalIgnoreCase) ? OtpKind.Hotp
            : parsed.Host.Equals("totp", StringComparison.OrdinalIgnoreCase) ? OtpKind.Totp
            : throw new FormatException($"'{parsed.Host}' is not a kind of one-time password AuthGeek knows.");

        var query = ParseQuery(parsed.Query);

        if (!query.TryGetValue("secret", out var secret) || string.IsNullOrWhiteSpace(secret))
            throw new FormatException("That link has no secret in it, so there is nothing to make a code from.");

        if (!Base32.LooksValid(secret))
            throw new FormatException("The secret in that link is not valid base32.");

        // The path is /Issuer:label or just /label, and both halves are URL encoded.
        //
        // The order here is the whole trick. The separator has to be found in the ESCAPED text
        // and each half unescaped afterwards. Unescaping first turns every %3A into a real colon,
        // and an account named "a:b@x.com" then splits in the wrong place and comes out as
        // somebody else's issuer. The raw path is taken from the original string rather than from
        // Uri.AbsolutePath, because that property is allowed to unescape and doing this correctly
        // must not depend on which build of .NET decided to.
        var rawPath = RawPath(text);
        string pathIssuer = "", label = rawPath;
        var colon = rawPath.IndexOf(':');
        if (colon > 0)
        {
            pathIssuer = rawPath[..colon];
            label = rawPath[(colon + 1)..];
        }

        pathIssuer = Uri.UnescapeDataString(pathIssuer);
        label = Uri.UnescapeDataString(label).TrimStart();

        var issuer = query.TryGetValue("issuer", out var q) && !string.IsNullOrWhiteSpace(q) ? q : pathIssuer;

        return new Account
        {
            Issuer = issuer.Trim(),
            Label = label.Trim(),
            Secret = secret.Replace(" ", "").Replace("-", "").ToUpperInvariant(),
            Kind = kind,
            Algorithm = ReadAlgorithm(query),
            Digits = ReadInt(query, "digits", 6, 6, 10),
            Period = ReadInt(query, "period", 30, 1, 300),
            Counter = kind == OtpKind.Hotp ? ReadLong(query, "counter", 0) : 0
        };
    }

    /// <summary>Writes the URI back out. Used by export, and to make a QR for another device.</summary>
    public static string Format(Account account)
    {
        var kind = account.Kind == OtpKind.Hotp ? "hotp" : "totp";

        var path = string.IsNullOrWhiteSpace(account.Issuer)
            ? Uri.EscapeDataString(account.Label)
            : Uri.EscapeDataString(account.Issuer) + ":" + Uri.EscapeDataString(account.Label);

        var sb = new StringBuilder($"otpauth://{kind}/{path}?secret={account.Secret}");

        if (!string.IsNullOrWhiteSpace(account.Issuer))
            sb.Append("&issuer=").Append(Uri.EscapeDataString(account.Issuer));

        if (account.Algorithm != OtpAlgorithm.Sha1)
            sb.Append("&algorithm=").Append(account.Algorithm.ToString().ToUpperInvariant());

        if (account.Digits != 6) sb.Append("&digits=").Append(account.Digits);

        if (account.Kind == OtpKind.Totp)
        {
            if (account.Period != 30) sb.Append("&period=").Append(account.Period);
        }
        else
        {
            sb.Append("&counter=").Append(account.Counter);
        }

        return sb.ToString();
    }

    /// <summary>The path portion, still escaped, exactly as it was written.</summary>
    private static string RawPath(string uri)
    {
        var afterScheme = uri.IndexOf("://", StringComparison.Ordinal) + 3;
        var slash = uri.IndexOf('/', afterScheme);
        if (slash < 0) return "";

        var end = uri.IndexOf('?', slash);
        var path = end < 0 ? uri[(slash + 1)..] : uri[(slash + 1)..end];
        return path;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            result[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }

        return result;
    }

    private static OtpAlgorithm ReadAlgorithm(IReadOnlyDictionary<string, string> query)
    {
        if (!query.TryGetValue("algorithm", out var a)) return OtpAlgorithm.Sha1;

        return a.ToUpperInvariant() switch
        {
            "SHA256" => OtpAlgorithm.Sha256,
            "SHA512" => OtpAlgorithm.Sha512,
            _ => OtpAlgorithm.Sha1
        };
    }

    /// <summary>
    /// A value out of range is replaced by the default rather than refused. A link with digits=99
    /// is a broken link, but the secret in it is still good, and refusing the whole thing would
    /// lose somebody an account for the sake of a field they never chose.
    /// </summary>
    private static int ReadInt(IReadOnlyDictionary<string, string> query, string key, int fallback, int min, int max)
        => query.TryGetValue(key, out var raw) && int.TryParse(raw, out var v) && v >= min && v <= max ? v : fallback;

    private static long ReadLong(IReadOnlyDictionary<string, string> query, string key, long fallback)
        => query.TryGetValue(key, out var raw) && long.TryParse(raw, out var v) && v >= 0 ? v : fallback;
}
