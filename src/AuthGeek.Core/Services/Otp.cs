using System.Security.Cryptography;

namespace AuthGeek.Core.Services;

/// <summary>
/// One-time passwords, to the standards rather than to a guess.
///
/// HOTP is RFC 4226: HMAC the counter, take four bytes from an offset the HMAC itself chooses,
/// and read them as a number. TOTP is RFC 6238: the same thing with the counter being the number
/// of time steps since 1970.
///
/// This is the one piece of AuthGeek that has a single correct answer, and both RFCs publish
/// test vectors for it, so it is checked against them rather than against itself.
/// </summary>
public static class Otp
{
    /// <summary>The counter-based one. RFC 4226.</summary>
    public static string Hotp(byte[] secret, long counter, int digits = 6, OtpAlgorithm algorithm = OtpAlgorithm.Sha1)
    {
        if (digits is < 6 or > 10) throw new ArgumentOutOfRangeException(nameof(digits), digits, "Six to ten digits.");

        var message = new byte[8];
        for (var i = 7; i >= 0; i--)
        {
            message[i] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        using HMAC hmac = algorithm switch
        {
            OtpAlgorithm.Sha256 => new HMACSHA256(secret),
            OtpAlgorithm.Sha512 => new HMACSHA512(secret),
            _ => new HMACSHA1(secret)
        };

        var hash = hmac.ComputeHash(message);

        // Dynamic truncation. The low four bits of the last byte pick where to read from, which
        // is what stops the same four bytes being used every time.
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | ((hash[offset + 1] & 0xFF) << 16)
                     | ((hash[offset + 2] & 0xFF) << 8)
                     | (hash[offset + 3] & 0xFF);

        var modulo = (int)Math.Pow(10, digits);
        return (binary % modulo).ToString().PadLeft(digits, '0');
    }

    /// <summary>The time-based one. RFC 6238.</summary>
    public static string Totp(byte[] secret, DateTimeOffset at, int period = 30, int digits = 6,
        OtpAlgorithm algorithm = OtpAlgorithm.Sha1)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period), period, "At least one second.");
        return Hotp(secret, at.ToUnixTimeSeconds() / period, digits, algorithm);
    }

    /// <summary>How many seconds are left on the current code.</summary>
    public static int SecondsRemaining(DateTimeOffset at, int period = 30) =>
        period - (int)(at.ToUnixTimeSeconds() % period);
}

public enum OtpAlgorithm
{
    Sha1,
    Sha256,
    Sha512
}
