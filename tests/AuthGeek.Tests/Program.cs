using System.Text;
using AuthGeek.Core.Models;
using AuthGeek.Core.Services;

// A plain console harness rather than a test framework, matching the other apps in the range:
// it runs in CI, exits non-zero on failure, and adds no dependency.
int failed = 0;
void Check(string name, bool ok, string? detail = null)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok || detail is null ? "" : "  -> " + detail)}");
    if (!ok) failed++;
}

var tmp = Path.Combine(Path.GetTempPath(), "ag-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tmp);

// ---- RFC 4226 appendix D ------------------------------------------------------------
// The published HOTP test vectors, verbatim. If these do not pass, nothing else matters,
// because every code the application ever shows would be wrong.
{
    var secret = Encoding.ASCII.GetBytes("12345678901234567890");
    string[] expected =
    {
        "755224", "287082", "359152", "969429", "338314",
        "254676", "287922", "162583", "399871", "520489"
    };

    var allRight = true;
    for (var counter = 0; counter < expected.Length; counter++)
    {
        var got = Otp.Hotp(secret, counter);
        if (got == expected[counter]) continue;
        allRight = false;
        Check($"RFC 4226 counter {counter}", false, $"expected {expected[counter]}, got {got}");
    }

    Check("RFC 4226, all ten published HOTP vectors", allRight);
}

// ---- RFC 6238 appendix B ------------------------------------------------------------
// The published TOTP vectors, at eight digits, across all three hash functions. The seeds in
// the RFC are the ASCII string repeated to the length each hash needs.
{
    var sha1 = Encoding.ASCII.GetBytes("12345678901234567890");
    var sha256 = Encoding.ASCII.GetBytes("12345678901234567890123456789012");
    var sha512 = Encoding.ASCII.GetBytes("1234567890123456789012345678901234567890123456789012345678901234");

    (long Time, string Sha1, string Sha256, string Sha512)[] vectors =
    {
        (59,          "94287082", "46119246", "90693936"),
        (1111111109,  "07081804", "68084774", "25091201"),
        (1111111111,  "14050471", "67062674", "99943326"),
        (1234567890,  "89005924", "91819424", "93441116"),
        (2000000000,  "69279037", "90698825", "38618901"),
        (20000000000, "65353130", "77737706", "47863826")
    };

    var allRight = true;
    foreach (var v in vectors)
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(v.Time);

        foreach (var (name, secret, want, algorithm) in new[]
                 {
                     ("SHA1",   sha1,   v.Sha1,   OtpAlgorithm.Sha1),
                     ("SHA256", sha256, v.Sha256, OtpAlgorithm.Sha256),
                     ("SHA512", sha512, v.Sha512, OtpAlgorithm.Sha512)
                 })
        {
            var got = Otp.Totp(secret, at, period: 30, digits: 8, algorithm: algorithm);
            if (got == want) continue;
            allRight = false;
            Check($"RFC 6238 {name} at {v.Time}", false, $"expected {want}, got {got}");
        }
    }

    Check("RFC 6238, all eighteen published TOTP vectors across SHA1, SHA256 and SHA512", allRight);
}

// ---- Countdown ------------------------------------------------------------------------
{
    Check("a code has 30 seconds left the moment it changes",
        Otp.SecondsRemaining(DateTimeOffset.FromUnixTimeSeconds(60)) == 30);
    Check("a code has 1 second left just before it changes",
        Otp.SecondsRemaining(DateTimeOffset.FromUnixTimeSeconds(89)) == 1);
    Check("a 60 second code counts down from 60",
        Otp.SecondsRemaining(DateTimeOffset.FromUnixTimeSeconds(120), 60) == 60);
}

// ---- Base32 ----------------------------------------------------------------------------
{
    // RFC 4648 test vectors
    Check("base32 decodes 'f'", Encoding.ASCII.GetString(Base32.Decode("MY======")) == "f");
    Check("base32 decodes 'foobar'", Encoding.ASCII.GetString(Base32.Decode("MZXW6YTBOI======")) == "foobar");
    Check("base32 encodes 'foobar'", Base32.Encode(Encoding.ASCII.GetBytes("foobar")) == "MZXW6YTBOI======");

    // The way people actually paste them
    Check("spaces in a pasted secret are ignored",
        Base32.Decode("MZXW 6YTB OI").SequenceEqual(Base32.Decode("MZXW6YTBOI")));
    Check("lower case is accepted",
        Base32.Decode("mzxw6ytboi").SequenceEqual(Base32.Decode("MZXW6YTBOI")));
    Check("missing padding is accepted", Base32.Decode("MZXW6YTBOI").Length == 6);

    Check("a secret with a 1 or a 0 in it is refused", !Base32.LooksValid("MZXW10TB"));
    Check("an empty secret is refused", !Base32.LooksValid("   "));
    Check("base32 round trips arbitrary bytes",
        Base32.Decode(Base32.Encode(new byte[] { 1, 2, 3, 250, 251, 252, 0, 255 }))
            .SequenceEqual(new byte[] { 1, 2, 3, 250, 251, 252, 0, 255 }));
}

// ---- otpauth URIs ------------------------------------------------------------------------
{
    var a = OtpUri.Parse("otpauth://totp/GitHub:andy%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub");
    Check("the issuer is read", a.Issuer == "GitHub", a.Issuer);
    Check("the label is unescaped", a.Label == "andy@example.com", a.Label);
    Check("the secret is read", a.Secret == "JBSWY3DPEHPK3PXP", a.Secret);
    Check("the defaults are the usual ones",
        a is { Kind: OtpKind.Totp, Algorithm: OtpAlgorithm.Sha1, Digits: 6, Period: 30 });

    // The issuer parameter wins over the path prefix, which is what the spec says.
    var b = OtpUri.Parse("otpauth://totp/Old:me?secret=JBSWY3DPEHPK3PXP&issuer=New");
    Check("the issuer parameter beats the path prefix", b.Issuer == "New", b.Issuer);

    var c = OtpUri.Parse("otpauth://totp/me?secret=JBSWY3DPEHPK3PXP&algorithm=SHA512&digits=8&period=60");
    Check("algorithm, digits and period are read",
        c is { Algorithm: OtpAlgorithm.Sha512, Digits: 8, Period: 60 });

    var d = OtpUri.Parse("otpauth://hotp/bank?secret=JBSWY3DPEHPK3PXP&counter=42");
    Check("counter based accounts are read", d is { Kind: OtpKind.Hotp, Counter: 42 });

    // A label with a colon in it must not come apart
    var e = OtpUri.Parse("otpauth://totp/Acme%3A%20Ltd:a%3Ab%40x.com?secret=JBSWY3DPEHPK3PXP");
    Check("an escaped colon in the label survives", e.Label == "a:b@x.com", e.Label);

    var problems = new (string Uri, string Why)[]
    {
        ("", "empty"),
        ("https://example.com", "not otpauth"),
        ("otpauth://totp/me", "no secret"),
        ("otpauth://totp/me?secret=", "empty secret"),
        ("otpauth://totp/me?secret=NOT!BASE32", "bad secret"),
        ("otpauth://banana/me?secret=JBSWY3DPEHPK3PXP", "unknown kind")
    };

    var allRefused = true;
    foreach (var p in problems)
    {
        try { OtpUri.Parse(p.Uri); allRefused = false; Check($"refuses {p.Why}", false, p.Uri); }
        catch (FormatException) { }
    }
    Check("every kind of broken link is refused with a reason", allRefused);

    // A broken optional field must not lose the account
    var f = OtpUri.Parse("otpauth://totp/me?secret=JBSWY3DPEHPK3PXP&digits=99&period=banana");
    Check("a nonsense digits or period falls back rather than losing the account",
        f is { Digits: 6, Period: 30 });

    // Round trip
    foreach (var original in new[] { a, c, d, e })
    {
        var back = OtpUri.Parse(OtpUri.Format(original));
        Check($"{original.DisplayName} round trips through a URI",
            back.Secret == original.Secret && back.Issuer == original.Issuer && back.Label == original.Label
            && back.Digits == original.Digits && back.Period == original.Period
            && back.Algorithm == original.Algorithm && back.Kind == original.Kind
            && back.Counter == original.Counter);
    }
}


// ---- Google Authenticator transfer ------------------------------------------------------
// The payload below was built by an independent encoder rather than by AuthGeek, so this checks
// the reader against the real wire format and not against itself. It holds four accounts: a
// normal one, a counter based one at eight digits and SHA256, one with no issuer at all, and one
// carrying two fields this reader has never heard of.
{
    const string data = "CjMKCkhlbGxvId6tvu8SF0dpdEh1YjphbmR5QGV4YW1wbGUuY29tGgZHaXRIdWIgASgBMAIKKwoGZm9vYmFy" +
                        "Eg1iYW5rLTEyMzQ1Njc4GgpOYXRpb253aWRlIAIoAjABOCoKIAoKSGVsbG8h3q2%2B7xIQc29sb0BleGFtcGxl" +
                        "LmNvbTACCiYKCkhlbGxvId6tvu8SBmZ1dHVyZTACmAG5YKIBCXdobyBrbm93cxABGAEgACiGpDw%3D";

    var result = GoogleMigration.Parse("otpauth-migration://offline?data=" + data);

    Check("all four accounts are read", result.Count == 4, result.Count + " read, " + string.Join("; ", result.Problems));
    Check("nothing was reported as a problem", result.Problems.Count == 0, string.Join("; ", result.Problems));

    if (result.Count == 4)
    {
        var g = result.Accounts[0];
        Check("the secret survives the transfer", g.Secret == "JBSWY3DPEHPK3PXP", g.Secret);
        Check("the issuer is read", g.Issuer == "GitHub", g.Issuer);
        Check("the duplicated issuer prefix is stripped off the label",
            g.Label == "andy@example.com", g.Label);

        var b = result.Accounts[1];
        Check("a counter based transfer keeps its counter and its settings",
            b is { Kind: OtpKind.Hotp, Counter: 42, Digits: 8, Algorithm: OtpAlgorithm.Sha256 }
            && b.Secret == "MZXW6YTBOI",
            $"{b.Kind} {b.Counter} {b.Digits} {b.Algorithm} {b.Secret}");

        var solo = result.Accounts[2];
        Check("an account with no issuer keeps its label rather than losing it",
            solo.Issuer == "" && solo.Label == "solo@example.com", $"'{solo.Issuer}' '{solo.Label}'");

        var future = result.Accounts[3];
        Check("fields this reader has never seen are skipped rather than failing the import",
            future.Label == "future" && future.Secret == "JBSWY3DPEHPK3PXP", future.Label);

        // The imported account must produce the same code the original one did.
        var at = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        Check("an imported account produces the code the original did",
            Otp.Totp(Base32.Decode(g.Secret), at) == Otp.Totp(Base32.Decode("JBSWY3DPEHPK3PXP"), at));
    }

    Check("a transfer link is recognised", GoogleMigration.Looks("otpauth-migration://offline?data=AA"));
    Check("an otpauth link is not mistaken for one", !GoogleMigration.Looks("otpauth://totp/x?secret=AA"));

    foreach (var (bad, why) in new[]
             {
                 ("otpauth://totp/x?secret=JBSWY3DPEHPK3PXP", "an otpauth link"),
                 ("otpauth-migration://offline", "no data"),
                 ("otpauth-migration://offline?data=!!!not base64!!!", "bad base64")
             })
    {
        try { GoogleMigration.Parse(bad); Check($"refuses {why}", false, bad); }
        catch (FormatException) { Check($"refuses {why}", true); }
    }
}


// ---- QR codes ---------------------------------------------------------------------------
{
    const string uri = "otpauth://totp/GitHub:andy%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub";

    var (rgb, w, h) = QrCode.DrawAsPixels(uri);
    Check("a QR code is drawn", w > 0 && h > 0 && rgb.Length == w * h * 3, $"{w}x{h}");

    var read = QrCode.Read(rgb, w, h);
    Check("the drawn QR reads back as exactly what went in", read == uri, read ?? "nothing found");

    if (read is not null)
    {
        var account = OtpUri.Parse(read);
        Check("an account survives a trip through a QR code",
            account.Secret == "JBSWY3DPEHPK3PXP" && account.Issuer == "GitHub"
            && account.Label == "andy@example.com");
    }

    // Plain white pixels are not a QR code, and must come back as "nothing here" rather than
    // as an exception, because the user is going to point this at things that are not QR codes.
    var blank = new byte[100 * 100 * 3];
    Array.Fill(blank, (byte)255);
    Check("a picture with no QR code in it finds nothing rather than failing",
        QrCode.Read(blank, 100, 100) is null);

    // A Google transfer QR is much denser. Worth proving it survives the round trip too.
    const string migration = "otpauth-migration://offline?data=CjMKCkhlbGxvId6tvu8SF0dpdEh1YjphbmR5QGV4YW1wbGUuY29tGgZHaXRIdWIgASgBMAIQARgBIAA=";
    var (mrgb, mw, mh) = QrCode.DrawAsPixels(migration, scale: 6);
    Check("a Google transfer QR round trips", QrCode.Read(mrgb, mw, mh) == migration);
}

// ---- The vault. The part that must never lose anything. --------------------------------------
{
    var path = Path.Combine(tmp, "test.authgeek");
    const string password = "correct horse battery staple";

    var accounts = new List<Account>
    {
        new() { Issuer = "GitHub", Label = "andy@example.com", Secret = "JBSWY3DPEHPK3PXP" },
        new() { Issuer = "Bank", Label = "12345678", Secret = "MZXW6YTBOI", Kind = OtpKind.Hotp, Counter = 7 },
        new() { Issuer = "Work", Label = "a:b@x.com", Secret = "JBSWY3DPEHPK3PXP", Digits = 8, Period = 60,
                Algorithm = OtpAlgorithm.Sha512 }
    };

    Vault.Save(accounts, password, path);
    Check("a vault is written", File.Exists(path));

    var reopened = Vault.Open(password, path);
    Check("everything comes back", reopened.Count == 3, reopened.Count.ToString());
    Check("the secrets come back byte for byte",
        reopened.Select(a => a.Secret).SequenceEqual(accounts.Select(a => a.Secret)));
    Check("the awkward fields come back",
        reopened[2] is { Digits: 8, Period: 60, Algorithm: OtpAlgorithm.Sha512 } &&
        reopened[1] is { Kind: OtpKind.Hotp, Counter: 7 });
    Check("the ids come back, so a rename is not a delete",
        reopened.Select(a => a.Id).SequenceEqual(accounts.Select(a => a.Id)));

    // The codes a restored vault produces must be identical to the ones the original did.
    var when = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    Check("a restored account produces the same code as the original",
        Otp.Totp(Base32.Decode(reopened[0].Secret), when) == Otp.Totp(Base32.Decode(accounts[0].Secret), when));

    // Nothing readable on disk
    var raw = File.ReadAllText(path);
    Check("no secret is readable in the file", !raw.Contains("JBSWY3DPEHPK3PXP") && !raw.Contains("MZXW6YTBOI"));
    Check("no account name is readable in the file", !raw.Contains("andy@example.com") && !raw.Contains("GitHub"));
    Check("the file says how it was encrypted, so an old vault still opens later",
        raw.Contains("argon2id") && raw.Contains("MemoryKib"));

    // Wrong password
    try
    {
        Vault.Open("not the password", path);
        Check("a wrong password is refused", false);
    }
    catch (WrongPasswordException) { Check("a wrong password is refused", true); }

    // Tampering
    var tampered = Path.Combine(tmp, "tampered.authgeek");
    var text = File.ReadAllText(path);
    var marker = "\"Payload\": \"";
    var at = text.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
    var flipped = text[..at] + (text[at] == 'A' ? 'B' : 'A') + text[(at + 1)..];
    File.WriteAllText(tampered, flipped);

    try
    {
        Vault.Open(password, tampered);
        Check("a tampered vault is refused rather than opened with altered contents", false);
    }
    catch (WrongPasswordException) { Check("a tampered vault is refused rather than opened with altered contents", true); }

    // A damaged file is told apart from a wrong password
    var damaged = Path.Combine(tmp, "damaged.authgeek");
    File.WriteAllText(damaged, "this is not json at all");
    try
    {
        Vault.Open(password, damaged);
        Check("a damaged file is reported as damaged, not as a wrong password", false);
    }
    catch (VaultDamagedException) { Check("a damaged file is reported as damaged, not as a wrong password", true); }

    // Every save must use a fresh salt and nonce
    Vault.Save(accounts, password, path);
    var second = File.ReadAllText(path);
    Check("every save uses a fresh salt and nonce", ExtractField(raw, "Salt") != ExtractField(second, "Salt")
                                                    && ExtractField(raw, "Nonce") != ExtractField(second, "Nonce"));
    Check("the previous vault is kept as .bak", File.Exists(path + ".bak"));
    Check("the .bak still opens with the same password", Vault.Open(password, path + ".bak").Count == 3);
    Check("no temporary file is left behind", !File.Exists(path + ".tmp"));

    // Changing the password
    Vault.ChangePassword(password, "a different one entirely", path);
    Check("the new password opens it", Vault.Open("a different one entirely", path).Count == 3);
    try
    {
        Vault.Open(password, path);
        Check("the old password no longer opens it", false);
    }
    catch (WrongPasswordException) { Check("the old password no longer opens it", true); }

    // An empty vault is a real state, not an error
    var empty = Path.Combine(tmp, "empty.authgeek");
    Vault.Save(Array.Empty<Account>(), "x", empty);
    Check("an empty vault saves and opens", Vault.Open("x", empty).Count == 0);

    // A large vault, because somebody with 200 accounts should not be a surprise
    var many = Enumerable.Range(0, 200).Select(i => new Account
    {
        Issuer = "Service " + i, Label = $"user{i}@example.com", Secret = "JBSWY3DPEHPK3PXP"
    }).ToList();
    var big = Path.Combine(tmp, "big.authgeek");
    Vault.Save(many, "x", big);
    Check("two hundred accounts round trip", Vault.Open("x", big).Count == 200);
}


// ---- Backup and restore. The reason this app was allowed to be built at all. --------------
{
    var dir = Path.Combine(tmp, "backup");
    Directory.CreateDirectory(dir);

    var original = new List<Account>
    {
        new() { Issuer = "GitHub", Label = "andy@example.com", Secret = "JBSWY3DPEHPK3PXP" },
        new() { Issuer = "Nationwide", Label = "12345678", Secret = "MZXW6YTBOI",
                Kind = OtpKind.Hotp, Counter = 9, Digits = 8 },
        new() { Issuer = "Work VPN", Label = "a:b@x.com", Secret = "JBSWY3DPEHPK3PXP",
                Algorithm = OtpAlgorithm.Sha512, Period = 60 },
        new() { Issuer = "", Label = "no issuer at all", Secret = "MZXW6YTBOI" }
    };

    var at = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    string CodeFor(Account a) => a.Kind == OtpKind.Hotp
        ? Otp.Hotp(Base32.Decode(a.Secret), a.Counter, a.Digits, a.Algorithm)
        : Otp.Totp(Base32.Decode(a.Secret), at, a.Period, a.Digits, a.Algorithm);

    var before = original.Select(CodeFor).ToList();

    // --- the encrypted route, which is what a real restore looks like ---
    var encrypted = Path.Combine(dir, "backup.authgeek");
    Backup.WriteEncrypted(original, "backup password", encrypted);

    var restored = Backup.ReadEncrypted("backup password", encrypted);
    Check("an encrypted backup restores every account", restored.Count == original.Count, restored.Count.ToString());
    Check("every restored account produces exactly the code the original did",
        restored.Select(CodeFor).SequenceEqual(before),
        string.Join(",", restored.Select(CodeFor)) + " vs " + string.Join(",", before));

    // --- the plain text route, which is how somebody leaves for another app ---
    var text = Path.Combine(dir, "export.txt");
    Backup.WriteText(original, text);

    var body = File.ReadAllText(text);
    Check("the text export warns, in capitals, what is in the file",
        body.Contains("PLAIN TEXT") && body.Contains("#"));

    var fromText = Backup.ReadText(text);
    Check("a text export reads back completely", fromText.Count == original.Count,
        fromText.Count + " read, " + string.Join("; ", fromText.Problems));
    Check("nothing in the text export failed to parse", fromText.Problems.Count == 0,
        string.Join("; ", fromText.Problems));
    Check("every account from the text export produces the code the original did",
        fromText.Accounts.Select(CodeFor).SequenceEqual(before),
        string.Join(",", fromText.Accounts.Select(CodeFor)));

    // --- a restore into an empty install, which is the case that actually matters ---
    var freshVault = Path.Combine(dir, "fresh.authgeek");
    var merged = Backup.Merge(Array.Empty<Account>(), restored.ToList());
    Vault.Save(merged.Accounts, "a new password on a new machine", freshVault);

    var onNewMachine = Vault.Open("a new password on a new machine", freshVault);
    Check("a restore onto a fresh install with a different password works",
        onNewMachine.Select(CodeFor).SequenceEqual(before), onNewMachine.Count.ToString());

    // --- broken lines are reported, never skipped in silence ---
    var mixed = Backup.ReadLines(new[]
    {
        "# a comment",
        "",
        "otpauth://totp/Good:one?secret=JBSWY3DPEHPK3PXP",
        "this line is rubbish",
        "otpauth://totp/NoSecret",
        "otpauth://totp/Good:two?secret=MZXW6YTBOI"
    });
    Check("the good lines are read", mixed.Count == 2, mixed.Count.ToString());
    Check("both bad lines are reported with their line number",
        mixed.Problems.Count == 2 && mixed.Problems.All(p => p.StartsWith("Line ")),
        string.Join("; ", mixed.Problems));

    // A Google transfer link in a text file works too
    var withTransfer = Backup.ReadLines(new[]
    {
        "otpauth-migration://offline?data=CjMKCkhlbGxvId6tvu8SF0dpdEh1YjphbmR5QGV4YW1wbGUuY29tGgZHaXRIdWIgASgBMAIQARgBIAA="
    });
    Check("a Google transfer link inside a text file is read", withTransfer.Count == 1,
        withTransfer.Count + " " + string.Join(";", withTransfer.Problems));
}

// ---- Merging, which must never destroy a working secret ------------------------------------
{
    var existing = new List<Account>
    {
        new() { Issuer = "GitHub", Label = "andy@example.com", Secret = "JBSWY3DPEHPK3PXP" },
        new() { Issuer = "Bank", Label = "me", Secret = "MZXW6YTBOI" }
    };

    // The same backup imported twice must not double everything.
    var again = Backup.Merge(existing, existing.Select(a => a.Copy()).ToList());
    Check("importing the same backup twice adds nothing",
        again.Accounts.Count == 2 && again.Added == 0 && again.AlreadyThere == 2,
        $"{again.Accounts.Count} total, {again.Added} added, {again.AlreadyThere} skipped");

    // Same name, different secret: both must survive.
    var clash = Backup.Merge(existing, new List<Account>
    {
        new() { Issuer = "GitHub", Label = "andy@example.com", Secret = "MZXW6YTBOI" }
    });
    Check("a different secret under a name already in use is kept, not merged over the old one",
        clash.Accounts.Count == 3 && clash.Added == 1, clash.Accounts.Count.ToString());
    Check("the original secret is untouched",
        clash.Accounts.Any(a => a.Issuer == "GitHub" && a.Secret == "JBSWY3DPEHPK3PXP"));
    Check("the new one is kept under a different name",
        clash.Accounts.Any(a => a.Issuer == "GitHub" && a.Secret == "MZXW6YTBOI"
                                && a.Label != "andy@example.com"));
    Check("the rename is explained rather than done quietly", clash.Notes.Count == 1,
        string.Join(";", clash.Notes));

    // A genuinely new account
    var fresh = Backup.Merge(existing, new List<Account>
    {
        new() { Issuer = "Fastmail", Label = "andy@example.com", Secret = "JBSWY3DPEHPK3PXP" }
    });
    Check("a new account is added", fresh.Accounts.Count == 3 && fresh.Added == 1);

    // Merging must never change what was already there
    Check("merging never alters an account that was already in the vault",
        fresh.Accounts.Take(2).Select(a => a.Secret).SequenceEqual(existing.Select(a => a.Secret)));
}

static string ExtractField(string json, string name)
{
    var marker = $"\"{name}\": \"";
    var start = json.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
    return json[start..json.IndexOf('"', start)];
}

Directory.Delete(tmp, true);

Console.WriteLine(failed == 0 ? "\nAll checks passed." : $"\n{failed} check(s) failed.");
return failed == 0 ? 0 : 1;
