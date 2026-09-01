using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using AuthGeek.ViewModels;

namespace AuthGeek.Views;

public partial class CodesView : UserControl
{
    public CodesView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private static AccountViewModel? RowOf(object? sender) =>
        (sender as Control)?.DataContext as AccountViewModel;

    /// <summary>
    /// Copies the code without the space in it. The space is there so a person can read the
    /// number; pasting it into a login box with a space in the middle would fail.
    /// </summary>
    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        var row = RowOf(sender);
        if (row is null || !row.HasCode) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        await clipboard.SetTextAsync(row.RawCode);

        if (sender is Button b)
        {
            b.Content = "Copied";
            await Task.Delay(1200);
            b.Content = "Copy";
        }
    }

    /// <summary>
    /// Shows the account as a QR code so it can be added to a phone. Behind a button rather than
    /// on the row, because it puts a working secret on screen.
    /// </summary>
    private async void OnShowQr(object? sender, RoutedEventArgs e)
    {
        var row = RowOf(sender);
        if (row is null || this.GetVisualRoot() is not Window window) return;

        await QrWindow.ShowAsync(window, row.Account);
    }

    private async void OnRemove(object? sender, RoutedEventArgs e)
    {
        var row = RowOf(sender);
        if (row is null || this.GetVisualRoot() is not Window window) return;
        if (DataContext is not ShellViewModel shell) return;

        var confirmed = await Confirm.ShowAsync(window,
            "Remove this account",
            $"Remove {row.Account.DisplayName} from AuthGeek?\n\n" +
            "The secret goes with it, and there is no undo. If this is the only copy you have, " +
            "you will not be able to sign in to that service again without recovering the account " +
            "through whatever the service itself offers.",
            "Remove it");

        if (confirmed) await Notice.ShowAsync(window, "Removed", shell.Remove(row), null);
    }
}
