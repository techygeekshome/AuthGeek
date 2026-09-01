using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using AuthGeek.Core.Models;
using AuthGeek.Core.Services;
using AuthGeek.ViewModels;

namespace AuthGeek.Views;

public partial class AddView : UserControl
{
    public AddView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private ShellViewModel? Vm => DataContext as ShellViewModel;

    private void Say(string message)
    {
        var box = this.FindControl<TextBlock>("Outcome")!;
        box.Text = message;
        box.IsVisible = message.Length > 0;
    }

    // ------------------------------------------------------------------ pasted links

    private void OnAddLink(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var box = this.FindControl<TextBox>("LinkBox")!;
        var text = box.Text ?? "";

        if (string.IsNullOrWhiteSpace(text))
        {
            Say("There is nothing in the box to add.");
            return;
        }

        var read = Backup.ReadLines(text.Split('\n'));

        if (read.Count == 0)
        {
            Say(read.Problems.Count > 0
                ? string.Join(" ", read.Problems)
                : "Nothing in there looked like an otpauth link.");
            return;
        }

        var outcome = Vm.AddAccounts(read.Accounts);
        box.Text = "";

        Say(read.Problems.Count > 0
            ? outcome + " Some lines could not be read: " + string.Join(" ", read.Problems)
            : outcome);
    }

    private void OnClearLink(object? sender, RoutedEventArgs e)
    {
        this.FindControl<TextBox>("LinkBox")!.Text = "";
        Say("");
    }

    // ------------------------------------------------------------------ a QR in a picture

    /// <summary>
    /// Loads the picture, hands its pixels to the reader, and adds whatever was in it.
    ///
    /// The image is decoded here rather than in the Core project, because the user interface
    /// already has a decoder and the Core should not gain an image library for one screen.
    /// </summary>
    private async void OnChooseImage(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a picture of a QR code",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Pictures")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" }
                },
                FilePickerFileTypes.All
            }
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;

        try
        {
            var (rgb, width, height) = ReadPixels(path);
            var text = QrCode.Read(rgb, width, height);

            if (text is null)
            {
                Say("No QR code was found in that picture. A tighter crop around the code, or a " +
                    "larger screenshot, usually does it.");
                return;
            }

            var read = Backup.ReadLines(new[] { text });

            if (read.Count == 0)
            {
                Say("That QR code was read, but it does not contain a two-factor account. " +
                    (read.Problems.Count > 0 ? string.Join(" ", read.Problems) : ""));
                return;
            }

            Say(Vm.AddAccounts(read.Accounts));
        }
        catch (Exception ex)
        {
            Log.Write("QR image: " + ex);
            Say("That picture could not be read: " + ex.Message);
        }
    }

    /// <summary>Decodes an image file to plain RGB bytes, which is all the reader wants.</summary>
    private static (byte[] Rgb, int Width, int Height) ReadPixels(string path)
    {
        using var bitmap = new Bitmap(path);

        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;

        var bgra = new byte[width * height * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(bgra,
            System.Runtime.InteropServices.GCHandleType.Pinned);

        try
        {
            bitmap.CopyPixels(new Avalonia.PixelRect(0, 0, width, height),
                handle.AddrOfPinnedObject(), bgra.Length, width * 4);
        }
        finally
        {
            handle.Free();
        }

        var rgb = new byte[width * height * 3];
        for (int i = 0, j = 0; i < bgra.Length; i += 4, j += 3)
        {
            rgb[j] = bgra[i + 2];
            rgb[j + 1] = bgra[i + 1];
            rgb[j + 2] = bgra[i];
        }

        return (rgb, width, height);
    }

    // ------------------------------------------------------------------ typed in by hand

    private void OnAddTyped(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var issuer = (this.FindControl<TextBox>("IssuerBox")!.Text ?? "").Trim();
        var label = (this.FindControl<TextBox>("LabelBox")!.Text ?? "").Trim();
        var secret = (this.FindControl<TextBox>("SecretBox")!.Text ?? "").Replace(" ", "").Replace("-", "").Trim();

        if (secret.Length == 0)
        {
            Say("A secret is the one thing that cannot be left out.");
            return;
        }

        if (!Base32.LooksValid(secret))
        {
            Say("That secret is not valid base32. These are made of the letters A to Z and the " +
                "digits 2 to 7 only, so a 0, 1 or 8 in there is usually a letter that has been " +
                "read wrong: O, I or B.");
            return;
        }

        if (issuer.Length == 0 && label.Length == 0)
        {
            Say("Give it a service or an account name, or the row will have nothing on it.");
            return;
        }

        var outcome = Vm.AddAccounts(new[]
        {
            new Account { Issuer = issuer, Label = label, Secret = secret.ToUpperInvariant() }
        });

        this.FindControl<TextBox>("IssuerBox")!.Text = "";
        this.FindControl<TextBox>("LabelBox")!.Text = "";
        this.FindControl<TextBox>("SecretBox")!.Text = "";

        Say(outcome);
    }
}
