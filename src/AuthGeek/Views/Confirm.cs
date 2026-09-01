using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AuthGeek.Views;

/// <summary>
/// A yes or no window for the handful of things in AuthGeek that cannot be undone.
///
/// Built in code rather than XAML because it is twenty lines of layout. The dangerous action is
/// named on its own button rather than being "OK", so nobody agrees to something by pressing the
/// button they always press.
/// </summary>
internal static class Confirm
{
    public static async Task<bool> ShowAsync(Window owner, string title, string message, string confirmText)
    {
        var answer = false;

        var window = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#0A0D16"),
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI, Arial")
        };

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse("#BEC4D2"),
            FontSize = 13
        };

        var go = new Button { Content = confirmText, Padding = new Thickness(14, 8) };
        go.Click += (_, _) => { answer = true; window.Close(); };

        var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 8) };
        cancel.Click += (_, _) => window.Close();

        window.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Children =
            {
                text,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new Thickness(0, 20, 0, 0),
                    Children = { cancel, go }
                }
            }
        };

        await window.ShowDialog(owner);
        return answer;
    }

    /// <summary>Asks for a password. Used by backup and restore, which need one of their own.</summary>
    public static async Task<string?> AskPasswordAsync(Window owner, string title, string message,
        string confirmText, bool twice)
    {
        string? answer = null;

        var window = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#0A0D16"),
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI, Arial")
        };

        var first = new TextBox { PasswordChar = '•', Watermark = "Password" };
        var second = new TextBox { PasswordChar = '•', Watermark = "Type it again", IsVisible = twice };
        var problem = new TextBlock
        {
            Foreground = Brush.Parse("#E8B45A"),
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };

        var go = new Button { Content = confirmText, Padding = new Thickness(14, 8) };
        go.Click += (_, _) =>
        {
            var value = first.Text ?? "";

            if (value.Length == 0)
            {
                problem.Text = "A backup with no password is a plain text file with a different name.";
                problem.IsVisible = true;
                return;
            }

            if (twice && value != (second.Text ?? ""))
            {
                problem.Text = "Those two do not match.";
                problem.IsVisible = true;
                return;
            }

            answer = value;
            window.Close();
        };

        var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 8) };
        cancel.Click += (_, _) => window.Close();

        var panel = new StackPanel { Margin = new Thickness(22), Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse("#BEC4D2"),
            FontSize = 13
        });
        panel.Children.Add(first);
        panel.Children.Add(second);
        panel.Children.Add(problem);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { cancel, go }
        });

        window.Content = panel;
        window.Opened += (_, _) => first.Focus();

        await window.ShowDialog(owner);
        return answer;
    }
}
