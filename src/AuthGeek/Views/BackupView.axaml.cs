using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using AuthGeek.Core.Services;
using AuthGeek.ViewModels;

namespace AuthGeek.Views;

public partial class BackupView : UserControl
{
    public BackupView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private ShellViewModel? Vm => DataContext as ShellViewModel;

    private void Say(string message)
    {
        var box = this.FindControl<TextBlock>("Outcome")!;
        box.Text = message;
        box.IsVisible = message.Length > 0;
    }

    private Window? Owner => this.GetVisualRoot() as Window;

    // ------------------------------------------------------------------ encrypted

    private async void OnWriteEncrypted(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || Owner is null) return;

        var accounts = Vm.Snapshot();
        if (accounts.Count == 0)
        {
            Say("There is nothing to back up yet.");
            return;
        }

        var password = await Confirm.AskPasswordAsync(Owner,
            "Password for the backup",
            "Choose a password for this backup file. It can be the same as your master password " +
            "or a different one. Whatever you pick, the file cannot be opened without it and " +
            "there is no way to reset it.",
            "Write the backup", twice: true);

        if (password is null) return;

        var file = await Owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Where to write the backup",
            SuggestedFileName = $"authgeek-backup-{DateTime.Now:yyyy-MM-dd}{Backup.EncryptedExtension}",
            DefaultExtension = Backup.EncryptedExtension.TrimStart('.'),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("AuthGeek backup") { Patterns = new[] { "*" + Backup.EncryptedExtension } }
            }
        });

        var path = file?.TryGetLocalPath();
        if (path is null) return;

        try
        {
            Backup.WriteEncrypted(accounts, password, path);

            // Read it straight back. Vault.Save already proves the file opens, but proving it
            // here as well means the sentence on screen is a fact rather than a hope.
            var check = Backup.ReadEncrypted(password, path);

            Say($"{check.Count} account{(check.Count == 1 ? "" : "s")} written to {Path.GetFileName(path)} " +
                "and read straight back to prove it opens. Put a copy somewhere that is not this computer.");
        }
        catch (Exception ex)
        {
            Log.Write("Backup: " + ex);
            Say("The backup could not be written: " + ex.Message);
        }
    }

    private async void OnRestore(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || Owner is null) return;

        var files = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a backup",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("AuthGeek backup")
                {
                    Patterns = new[] { "*" + Backup.EncryptedExtension, "*.bak" }
                },
                FilePickerFileTypes.All
            }
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;

        var password = await Confirm.AskPasswordAsync(Owner,
            "Password for that backup",
            $"Enter the password for {Path.GetFileName(path)}.",
            "Restore", twice: false);

        if (password is null) return;

        try
        {
            var accounts = Backup.ReadEncrypted(password, path);
            Say(Vm.AddAccounts(accounts));
        }
        catch (WrongPasswordException ex)
        {
            Say(ex.Message);
        }
        catch (VaultDamagedException ex)
        {
            Say(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Write("Restore: " + ex);
            Say("That backup could not be read: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------ plain text

    private async void OnWriteText(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || Owner is null) return;

        var accounts = Vm.Snapshot();
        if (accounts.Count == 0)
        {
            Say("There is nothing to export yet.");
            return;
        }

        var confirmed = await Confirm.ShowAsync(Owner,
            "Export as plain text",
            $"This writes {accounts.Count} working two-factor secrets into a file that anyone can " +
            "read. Anybody who gets hold of it can generate your codes.\n\n" +
            "It is the right thing to use when you are moving to another authenticator, and the " +
            "wrong thing to leave lying about. Delete it once you have finished with it, and do " +
            "not put it in cloud storage or send it by email.",
            "I understand, export it");

        if (!confirmed) return;

        var file = await Owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Where to write the export",
            SuggestedFileName = $"authgeek-export-{DateTime.Now:yyyy-MM-dd}.txt",
            DefaultExtension = "txt",
            FileTypeChoices = new[] { new FilePickerFileType("Text") { Patterns = new[] { "*.txt" } } }
        });

        var path = file?.TryGetLocalPath();
        if (path is null) return;

        try
        {
            Backup.WriteText(accounts, path);
            var back = Backup.ReadText(path);

            Say($"{back.Count} account{(back.Count == 1 ? "" : "s")} written to {Path.GetFileName(path)} " +
                "and read back to prove nothing was lost. Remember to delete it when you are done.");
        }
        catch (Exception ex)
        {
            Log.Write("Export: " + ex);
            Say("The export could not be written: " + ex.Message);
        }
    }

    private async void OnImportText(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || Owner is null) return;

        var files = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a text export",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Text") { Patterns = new[] { "*.txt" } },
                FilePickerFileTypes.All
            }
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;

        try
        {
            var read = Backup.ReadText(path);

            if (read.Count == 0)
            {
                Say(read.Problems.Count > 0
                    ? "Nothing in that file could be read. " + string.Join(" ", read.Problems)
                    : "There were no otpauth links in that file.");
                return;
            }

            var outcome = Vm.AddAccounts(read.Accounts);
            Say(read.Problems.Count > 0
                ? outcome + " Some lines could not be read: " + string.Join(" ", read.Problems)
                : outcome);
        }
        catch (Exception ex)
        {
            Log.Write("Import: " + ex);
            Say("That file could not be read: " + ex.Message);
        }
    }
}
