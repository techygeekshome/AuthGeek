# AuthGeek

Your two-factor codes, on your own computer.

A two-factor authenticator for the desktop. Add an account by pasting its link, by reading its QR
code out of a picture, or by transferring the lot across from Google Authenticator in one go.
Everything lives in one encrypted file on this machine: no account, no server, no sync, nothing
uploaded.

Part of the [TechyGeeksHome](https://techygeekshome.info/geek-tools/) range.

## Why

Authy withdrew its desktop app, which left a lot of people with their codes only on a phone. One
lost or broken phone and every account has to be recovered one at a time through whatever proof of
identity each service happens to accept, and some of them accept none.

## What it does

- TOTP and HOTP, SHA1, SHA256 and SHA512, six to ten digits, any period
- Add by pasting an `otpauth://` link, by choosing a picture of a QR code, or by typing it in
- Import everything from Google Authenticator's "Transfer accounts" QR in one go
- Show any account as a QR code, so it can be added to a phone
- Encrypted backup, plain text export, and restore, all built in
- Locks itself after a few minutes of not being used

## Backup and restore came first

This is the only application in the range where the backup was written before the feature it backs
up, and it is worth saying why. A two-factor secret cannot be recovered from anywhere. Lose it and
there is no "reset your password" email: it is gone, and every account it protected has to be
recovered through the service's own process.

So there are two ways out, and both are tested end to end on every build:

- **An encrypted backup**, which is the same format as the vault itself and opens with nothing
  more than AuthGeek and its password.
- **A plain text export**, one `otpauth://` link per line, which every other authenticator can
  read. It is readable secrets in a file, so AuthGeek makes you say so out loud before it writes
  one. Refusing to offer it would be worse: it would mean the only way out of AuthGeek was
  AuthGeek.

Restoring never replaces anything. Accounts are added to what is already there, an identical
account is skipped rather than duplicated, and an account with the same name but a different
secret is kept **as well as** the old one under a slightly different name, because those are two
different accounts and losing either would be worse than a tidy list.

## What it will not do

- **It does not sync, and it has no account.** There is no server to sign in to and nowhere for
  your secrets to go.
- **It cannot reset your master password.** Not a policy, a fact: the password is what the
  encryption key is made from. No recovery question, no email link, no back door, for you or for
  anyone else.
- **It does not lock you in.** Every account exports as a standard link or shows as a QR code.
- **It does not make up its own encryption.** All of it is ordinary, boring and public.

## How it is protected

| | |
|---|---|
| Master password to key | **Argon2id**, 64 MB, 3 passes, 4 lanes. The parameters are written into the file so an old vault still opens after they are raised |
| Accounts | **AES-256-GCM**, which authenticates as well as encrypts, so a tampered vault fails to open rather than opening with something altered in it |
| Salt and nonce | Fresh random on **every** save |
| Codes | **RFC 4226** and **RFC 6238**, checked on every build against the test vectors published in those documents |

Saving is the part that gets the most care, because there is no second copy anywhere. A save
writes to a temporary file, opens and decrypts it to prove it works, keeps the previous vault as
`.bak`, and only then replaces the real one. Nothing is ever written in place, so a crash halfway
through cannot leave half a vault.

## Requirements

Windows 10 version 1809 or later, 64-bit. The .NET runtime is bundled, so there is nothing to
install first.

## Building

```
dotnet build AuthGeek.sln -c Release
dotnet run --project tests/AuthGeek.Tests -c Release
build.cmd installer
```

## Licence

GPL-3.0. Free to use, including at work. No paid tier, ever. For a program that holds your
two-factor secrets, being able to read every line of it is the least it could offer.
