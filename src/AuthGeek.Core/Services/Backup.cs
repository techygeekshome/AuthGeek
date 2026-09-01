using System.Text;
using AuthGeek.Core.Models;

namespace AuthGeek.Core.Services;

/// <summary>
/// Getting accounts out, and getting them back in.
///
/// This exists before anything else in AuthGeek does, because a two-factor secret cannot be
/// recovered from anywhere. Lose the vault with no backup and the accounts are gone: not
/// "reset your password", gone, and every service has to be recovered one at a time through
/// whatever proof of identity it happens to accept.
///
/// So there are two ways out, and both are tested end to end:
///
/// - An **encrypted backup**, which is the same format as the vault itself and can simply be
///   opened as one. Nothing clever, and nothing that needs this exact build of AuthGeek to read.
/// - A **plain text export**, one otpauth link per line, which every other authenticator can
///   import. It is readable secrets in a file, so the application makes the user say so out loud
///   before it writes one, but refusing to offer it at all would be worse: it would mean the only
///   way out of AuthGeek was AuthGeek.
/// </summary>
public static class Backup
{
    public const string EncryptedExtension = ".authgeek";
    public const string TextExtension = ".txt";

    /// <summary>
    /// An encrypted backup. It is a vault file, so it opens with <see cref="Vault.Open"/> and
    /// needs nothing but the password.
    /// </summary>
    public static void WriteEncrypted(IEnumerable<Account> accounts, string password, string path)
        => Vault.Save(accounts, password, path);

    /// <summary>Reads an encrypted backup. The same thing as opening a vault.</summary>
    public static IReadOnlyList<Account> ReadEncrypted(string password, string path)
        => Vault.Open(password, path);

    /// <summary>
    /// A plain text export: one otpauth link per line, with a header saying plainly what the file
    /// is. The header lines start with # so they are skipped on the way back in and ignored by
    /// every other authenticator that reads this format.
    /// </summary>
    public static void WriteText(IEnumerable<Account> accounts, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# AuthGeek export");
        sb.AppendLine("# ");
        sb.AppendLine("# EVERY LINE BELOW CONTAINS A WORKING TWO-FACTOR SECRET IN PLAIN TEXT.");
        sb.AppendLine("# Anyone who reads this file can generate your codes. Keep it off cloud");
        sb.AppendLine("# storage and email, and delete it once you have finished with it.");
        sb.AppendLine("# ");
        sb.AppendLine($"# Written {DateTimeOffset.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine("# ");

        foreach (var account in accounts)
            sb.AppendLine(OtpUri.Format(account));

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>
    /// Reads a text export back. Anything that is not an otpauth link is reported rather than
    /// skipped in silence, because a line that did not come back is an account somebody is about
    /// to discover they have lost.
    /// </summary>
    public static ImportResult ReadText(string path) => ReadLines(File.ReadAllLines(path));

    public static ImportResult ReadLines(IEnumerable<string> lines)
    {
        var accounts = new List<Account>();
        var problems = new List<string>();
        var number = 0;

        foreach (var raw in lines)
        {
            number++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (GoogleMigration.Looks(line))
            {
                try
                {
                    var transferred = GoogleMigration.Parse(line);
                    accounts.AddRange(transferred.Accounts);
                    problems.AddRange(transferred.Problems);
                }
                catch (FormatException ex)
                {
                    problems.Add($"Line {number}: {ex.Message}");
                }
                continue;
            }

            try
            {
                accounts.Add(OtpUri.Parse(line));
            }
            catch (FormatException ex)
            {
                problems.Add($"Line {number}: {ex.Message}");
            }
        }

        return new ImportResult(accounts, problems);
    }

    /// <summary>
    /// Adds imported accounts to the ones already there, without losing either.
    ///
    /// The rules, in order:
    ///
    /// - Same issuer, same label and the same secret: already have it, skip it. Importing the
    ///   same backup twice should not double everything.
    /// - Same issuer and label but a **different** secret: keep both, and rename the new one.
    ///   These are not the same account, and quietly replacing one would destroy a working
    ///   secret to make room for another.
    /// - Anything else: add it.
    ///
    /// Nothing that was already in the vault is ever changed or removed by an import.
    /// </summary>
    public static MergeResult Merge(IReadOnlyList<Account> existing, IReadOnlyList<Account> incoming)
    {
        var result = new List<Account>(existing.Select(a => a.Copy()));
        var added = new List<Account>();
        var skipped = 0;
        var renamed = new List<string>();

        foreach (var candidate in incoming)
        {
            var sameName = result.Where(a =>
                string.Equals(a.Issuer, candidate.Issuer, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.Label, candidate.Label, StringComparison.OrdinalIgnoreCase)).ToList();

            if (sameName.Any(a => string.Equals(a.Secret, candidate.Secret, StringComparison.OrdinalIgnoreCase)))
            {
                skipped++;
                continue;
            }

            var account = candidate.Copy();

            if (sameName.Count > 0)
            {
                var suffix = 2;
                var baseLabel = account.Label;
                while (result.Any(a =>
                           string.Equals(a.Issuer, account.Issuer, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(a.Label, account.Label, StringComparison.OrdinalIgnoreCase)))
                {
                    account.Label = $"{baseLabel} ({suffix++})";
                }

                renamed.Add($"{account.Issuer} {baseLabel} was already there with a different secret, " +
                            $"so the new one was added as \"{account.Label}\" rather than replacing it.");
            }

            result.Add(account);
            added.Add(account);
        }

        return new MergeResult(result, added.Count, skipped, renamed);
    }
}

/// <param name="Accounts">Everything, old and new.</param>
/// <param name="Added">How many are new.</param>
/// <param name="AlreadyThere">How many were already there, identical, and were skipped.</param>
/// <param name="Notes">Anything the user needs to know about, one line each.</param>
public sealed record MergeResult(
    IReadOnlyList<Account> Accounts,
    int Added,
    int AlreadyThere,
    IReadOnlyList<string> Notes);
