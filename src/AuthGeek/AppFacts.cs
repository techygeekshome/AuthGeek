using TechyGeeksHome.Common;

namespace AuthGeek;

/// <summary>
/// Everything the shared About window and update check need to know about this app. One place,
/// so the wording here and the wording on the product page can be kept in step.
/// </summary>
internal static class AppFacts
{
    public static readonly AppInfo Info = new()
    {
        Name = "AuthGeek",
        Tagline = "Your two-factor codes, on your own computer",
        Description =
            "A two-factor authenticator for the desktop. Add an account by pasting its link, " +
            "reading its QR code from an image, or transferring the lot across from Google " +
            "Authenticator in one go. Everything is kept in one encrypted file on this machine: " +
            "no account, no server, no sync, nothing uploaded. Backup and restore are built in " +
            "and were the first things written, because a two-factor secret cannot be recovered " +
            "from anywhere if it is lost.",
        GitHubOwner = "techygeekshome",
        GitHubRepo = "AuthGeek",
        ProductUrl = "https://techygeekshome.info/authgeek/",
        IconUri = "avares://AuthGeek/Assets/authgeek.png",
        LicenceLine = "Free to use, including at work. GPL-3.0. No paid tier, ever.",
        Credits = new[]
        {
            new Credit("Argon2 by Konscious", "MIT", "https://github.com/kmaragon/Konscious.Security.Cryptography"),
            new Credit("ZXing.Net", "Apache-2.0", "https://github.com/micjahn/ZXing.Net"),
            new Credit("Avalonia", "MIT", "https://avaloniaui.net"),
            new Credit("RFC 4226 and RFC 6238", "The standards the codes are made to", "https://www.rfc-editor.org/rfc/rfc6238")
        }
    };
}
