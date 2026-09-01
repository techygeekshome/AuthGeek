namespace AuthGeek.Core.Services;

/// <summary>
/// RFC 4648 base32, which is how every service on earth writes a shared secret.
///
/// Written out rather than taken from a library because the whole thing is forty lines and it is
/// the one piece of decoding that stands between a user's typed secret and a working code. Case
/// is ignored, spaces are ignored, and padding is optional, because people copy these off web
/// pages in groups of four with spaces in and that has to work.
/// </summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] Decode(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new FormatException("That secret is empty.");

        var bits = 0;
        var value = 0;
        var output = new List<byte>();

        foreach (var raw in input)
        {
            if (raw is ' ' or '-' or '=' or '\t' or '\n' or '\r') continue;

            var c = char.ToUpperInvariant(raw);
            var index = Alphabet.IndexOf(c);
            if (index < 0)
                throw new FormatException(
                    $"'{raw}' is not part of a base32 secret. Secrets use A to Z and 2 to 7 only.");

            value = (value << 5) | index;
            bits += 5;

            if (bits < 8) continue;
            output.Add((byte)((value >> (bits - 8)) & 0xFF));
            bits -= 8;
        }

        if (output.Count == 0) throw new FormatException("That secret has nothing in it.");
        return output.ToArray();
    }

    /// <summary>Only used when writing an export out, so the padding is included.</summary>
    public static string Encode(byte[] data)
    {
        if (data.Length == 0) return "";

        var output = new System.Text.StringBuilder((data.Length + 4) / 5 * 8);
        var bits = 0;
        var value = 0;

        foreach (var b in data)
        {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Alphabet[(value >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0) output.Append(Alphabet[(value << (5 - bits)) & 31]);
        while (output.Length % 8 != 0) output.Append('=');

        return output.ToString();
    }

    /// <summary>True if the string could be a secret. Used to give a useful message before decoding.</summary>
    public static bool LooksValid(string input)
    {
        try
        {
            Decode(input);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
