using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuthGeek.Core.Models;
using Konscious.Security.Cryptography;

namespace AuthGeek.Core.Services;

/// <summary>
/// The encrypted store. Everything AuthGeek knows lives in one file, and this class is the only
/// thing that reads or writes it.
///
/// How it is protected:
///
/// - The master password becomes a key with **Argon2id**, not a plain hash and not PBKDF2.
///   Argon2id is deliberately expensive in memory as well as time, which is what makes a
///   graphics card no better at guessing than a processor. The parameters are stored in the file
///   so an old vault still opens after they are raised.
/// - The accounts are encrypted with **AES-256-GCM**, which authenticates as well as encrypts. A
///   vault that has been tampered with fails to open rather than opening with something altered
///   in it.
/// - A fresh random salt and nonce are written on **every save**. Reusing a nonce with GCM is the
///   one mistake that breaks it completely, so it is never derived from anything.
///
/// How it avoids losing anything, which matters more here than in any other app in the range,
/// because a lost secret cannot be recovered from anywhere:
///
/// - Writes go to a temporary file, are read back and decrypted to prove they work, and only then
///   replace the real one.
/// - The previous vault is kept as .bak before it is replaced.
/// - The file is never opened for writing in place, so a crash mid-save cannot leave half a vault.
/// </summary>
public sealed class Vault
{
    /// <summary>Bumped only if the format changes in a way an older build could not read.</summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Argon2id settings. 64 MB and three passes is the sort of cost that is unnoticeable once
    /// when unlocking and ruinous a few billion times over for somebody guessing.
    /// </summary>
    public const int DefaultMemoryKib = 65_536;
    public const int DefaultIterations = 3;
    public const int DefaultParallelism = 4;

    private const int SaltBytes = 16;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int KeyBytes = 32;

    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TechyGeeksHome", "AuthGeek");

    public static string DefaultPath { get; } = Path.Combine(DefaultDirectory, "accounts.authgeek");

    /// <summary>What is written to disk. The header is plain so the file can be opened at all.</summary>
    private sealed record VaultFile
    {
        public int Version { get; init; } = CurrentVersion;
        public string Kdf { get; init; } = "argon2id";
        public int MemoryKib { get; init; } = DefaultMemoryKib;
        public int Iterations { get; init; } = DefaultIterations;
        public int Parallelism { get; init; } = DefaultParallelism;
        public required string Salt { get; init; }
        public required string Nonce { get; init; }
        public required string Tag { get; init; }
        public required string Payload { get; init; }
        public DateTimeOffset Saved { get; init; } = DateTimeOffset.UtcNow;
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    // ------------------------------------------------------------------ key derivation

    /// <summary>
    /// Turns a password into a key. Slow on purpose; that is the entire point of it.
    /// </summary>
    private static byte[] DeriveKey(string password, byte[] salt, int memoryKib, int iterations, int parallelism)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKib,
            Iterations = iterations,
            DegreeOfParallelism = parallelism
        };

        return argon.GetBytes(KeyBytes);
    }

    // ------------------------------------------------------------------ reading

    public static bool Exists(string? path = null) => File.Exists(path ?? DefaultPath);

    /// <summary>
    /// Opens a vault. A wrong password and a damaged file are told apart, because "wrong password"
    /// and "your accounts are gone" need very different responses from the person reading it.
    /// </summary>
    public static IReadOnlyList<Account> Open(string password, string? path = null)
    {
        path ??= DefaultPath;

        if (!File.Exists(path))
            throw new FileNotFoundException("There is no vault at that path yet.", path);

        VaultFile file;
        try
        {
            file = JsonSerializer.Deserialize<VaultFile>(File.ReadAllText(path))
                   ?? throw new VaultDamagedException("That vault file is empty.");
        }
        catch (JsonException ex)
        {
            throw new VaultDamagedException(
                "That vault file could not be read. If a .bak file is next to it, that is the previous " +
                "version and is very likely intact.", ex);
        }

        if (file.Version > CurrentVersion)
            throw new VaultDamagedException(
                $"That vault was written by a newer version of AuthGeek (format {file.Version}). " +
                "Update AuthGeek and try again.");

        if (!string.Equals(file.Kdf, "argon2id", StringComparison.OrdinalIgnoreCase))
            throw new VaultDamagedException($"That vault uses '{file.Kdf}', which this build does not know.");

        var key = DeriveKey(password, Convert.FromBase64String(file.Salt),
            file.MemoryKib, file.Iterations, file.Parallelism);

        try
        {
            var plain = Decrypt(key,
                Convert.FromBase64String(file.Nonce),
                Convert.FromBase64String(file.Tag),
                Convert.FromBase64String(file.Payload));

            var accounts = JsonSerializer.Deserialize<List<Account>>(plain);
            return accounts ?? new List<Account>();
        }
        catch (CryptographicException)
        {
            // GCM cannot tell a wrong key from a tampered file, and neither can we. The message
            // says the likely thing first and the other possibility second, rather than picking.
            throw new WrongPasswordException(
                "That password did not open the vault. If you are certain it is right, the file may " +
                "have been altered or damaged, and the .bak file next to it is the previous version.");
        }
        catch (JsonException ex)
        {
            throw new VaultDamagedException("The vault opened but its contents could not be read.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    // ------------------------------------------------------------------ writing

    /// <summary>
    /// Saves the vault, and proves it saved before replacing what was there.
    ///
    /// The read-back is not paranoia. This is the only copy of secrets that cannot be recovered
    /// from anywhere else, and a save that silently produced an unopenable file would only be
    /// discovered the next time somebody needed to log in to something.
    /// </summary>
    public static void Save(IEnumerable<Account> accounts, string password, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var list = accounts.ToList();
        var plain = JsonSerializer.SerializeToUtf8Bytes(list);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var key = DeriveKey(password, salt, DefaultMemoryKib, DefaultIterations, DefaultParallelism);

        string json;
        try
        {
            var (cipher, tag) = Encrypt(key, nonce, plain);

            json = JsonSerializer.Serialize(new VaultFile
            {
                Salt = Convert.ToBase64String(salt),
                Nonce = Convert.ToBase64String(nonce),
                Tag = Convert.ToBase64String(tag),
                Payload = Convert.ToBase64String(cipher)
            }, Json);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, json);

        // Prove the file that is about to become the vault actually opens.
        try
        {
            var check = Open(password, temp);
            if (check.Count != list.Count)
                throw new VaultDamagedException(
                    $"The vault was written with {list.Count} accounts but read back with {check.Count}. " +
                    "Nothing has been replaced.");
        }
        catch (Exception)
        {
            TryDelete(temp);
            throw;
        }

        // Keep the previous version. Somebody who has just been through a bad save should have
        // something to go back to that is not "restore from a backup you did not make".
        if (File.Exists(path))
        {
            try
            {
                File.Copy(path, path + ".bak", overwrite: true);
            }
            catch (IOException)
            {
                // A backup we cannot write is not a reason to refuse the save.
            }
        }

        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Changes the master password. Same read-back and same backup as any other save.</summary>
    public static void ChangePassword(string currentPassword, string newPassword, string? path = null)
    {
        var accounts = Open(currentPassword, path);
        Save(accounts, newPassword, path);
    }

    // ------------------------------------------------------------------ the primitives

    private static (byte[] Cipher, byte[] Tag) Encrypt(byte[] key, byte[] nonce, byte[] plain)
    {
        var cipher = new byte[plain.Length];
        var tag = new byte[TagBytes];

        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(nonce, plain, cipher, tag);

        return (cipher, tag);
    }

    private static byte[] Decrypt(byte[] key, byte[] nonce, byte[] tag, byte[] cipher)
    {
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagBytes);
        aes.Decrypt(nonce, cipher, tag, plain);

        return plain;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>The password did not work. Recoverable: try again.</summary>
public sealed class WrongPasswordException : Exception
{
    public WrongPasswordException(string message) : base(message) { }
}

/// <summary>The file itself is wrong. Not recoverable by retyping anything.</summary>
public sealed class VaultDamagedException : Exception
{
    public VaultDamagedException(string message, Exception? inner = null) : base(message, inner) { }
}
