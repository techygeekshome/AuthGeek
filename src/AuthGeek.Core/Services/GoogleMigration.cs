using System.Text;
using AuthGeek.Core.Models;

namespace AuthGeek.Core.Services;

/// <summary>
/// Reads Google Authenticator's "Transfer accounts" export.
///
/// Google's export QR is not an otpauth link. It is
/// otpauth-migration://offline?data=&lt;base64 protobuf&gt;, and it can hold many accounts at once,
/// which is what makes it worth supporting: moving off Google Authenticator otherwise means
/// finding twenty original QR codes that no longer exist.
///
/// Google splits a large export across several QR codes. Each one is read on its own here and the
/// results added together, so somebody with forty accounts scans three images rather than being
/// told the first one is incomplete.
///
/// The message shape, which is public and stable:
///
///   MigrationPayload { repeated OtpParameters otp_parameters = 1; int32 version = 2; ... }
///   OtpParameters { bytes secret = 1; string name = 2; string issuer = 3;
///                   Algorithm algorithm = 4; DigitCount digits = 5; OtpType type = 6;
///                   int64 counter = 7; }
/// </summary>
public static class GoogleMigration
{
    public const string Scheme = "otpauth-migration";

    public static bool Looks(string text) =>
        text.TrimStart().StartsWith("otpauth-migration://", StringComparison.OrdinalIgnoreCase);

    public static ImportResult Parse(string uri)
    {
        var text = uri.Trim();

        if (!Looks(text))
            throw new FormatException("That is not a Google Authenticator transfer link.");

        var q = text.IndexOf("data=", StringComparison.OrdinalIgnoreCase);
        if (q < 0) throw new FormatException("That transfer link has no data in it.");

        var raw = text[(q + 5)..];
        var amp = raw.IndexOf('&');
        if (amp >= 0) raw = raw[..amp];

        byte[] payload;
        try
        {
            // The base64 is URL encoded inside the QR, so it has to be unescaped before it will
            // decode: a "+" that arrives as "%2B" is not the same byte.
            payload = Convert.FromBase64String(Uri.UnescapeDataString(raw));
        }
        catch (FormatException)
        {
            throw new FormatException("The data in that transfer link is not valid base64.");
        }

        var accounts = new List<Account>();
        var problems = new List<string>();

        var reader = new Protobuf(payload);
        while (reader.HasMore)
        {
            var (field, wire) = reader.ReadKey();

            if (field == 1 && wire == 2)
            {
                try
                {
                    accounts.Add(ReadAccount(reader.ReadBytes()));
                }
                catch (FormatException ex)
                {
                    problems.Add("One account in that transfer could not be read: " + ex.Message);
                }
            }
            else
            {
                reader.Skip(wire);
            }
        }

        if (accounts.Count == 0 && problems.Count == 0)
            problems.Add("That transfer link contained no accounts.");

        return new ImportResult(accounts, problems);
    }

    private static Account ReadAccount(ReadOnlySpan<byte> message)
    {
        byte[]? secret = null;
        string name = "", issuer = "";
        var algorithm = OtpAlgorithm.Sha1;
        var digits = 6;
        var kind = OtpKind.Totp;
        long counter = 0;

        var reader = new Protobuf(message);
        while (reader.HasMore)
        {
            var (field, wire) = reader.ReadKey();

            switch (field)
            {
                case 1 when wire == 2: secret = reader.ReadBytes().ToArray(); break;
                case 2 when wire == 2: name = Encoding.UTF8.GetString(reader.ReadBytes()); break;
                case 3 when wire == 2: issuer = Encoding.UTF8.GetString(reader.ReadBytes()); break;

                case 4 when wire == 0:
                    algorithm = reader.ReadVarint() switch
                    {
                        2 => OtpAlgorithm.Sha256,
                        3 => OtpAlgorithm.Sha512,
                        _ => OtpAlgorithm.Sha1        // 0 unspecified and 1 SHA1 both mean SHA1
                    };
                    break;

                case 5 when wire == 0:
                    digits = reader.ReadVarint() == 2 ? 8 : 6;
                    break;

                case 6 when wire == 0:
                    kind = reader.ReadVarint() == 1 ? OtpKind.Hotp : OtpKind.Totp;
                    break;

                case 7 when wire == 0:
                    counter = (long)reader.ReadVarint();
                    break;

                default:
                    reader.Skip(wire);
                    break;
            }
        }

        if (secret is null || secret.Length == 0)
            throw new FormatException("it had no secret in it");

        // Google writes the name as "Issuer:account" when it has both, and also sets the issuer
        // field. Stripping the duplicated prefix stops every imported account reading
        // "GitHub - GitHub:andy@example.com".
        var label = name;
        if (!string.IsNullOrEmpty(issuer) && label.StartsWith(issuer + ":", StringComparison.Ordinal))
            label = label[(issuer.Length + 1)..].TrimStart();

        return new Account
        {
            Issuer = issuer,
            Label = label,
            Secret = Base32.Encode(secret).TrimEnd('='),
            Kind = kind,
            Algorithm = algorithm,
            Digits = digits,
            Period = 30,                              // Google's export has no period field
            Counter = kind == OtpKind.Hotp ? counter : 0
        };
    }
}
