using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using AuthGeek.Core.Models;
using AuthGeek.Core.Services;

namespace AuthGeek.Views;

/// <summary>
/// Shows one account as a QR code, so it can be added to a phone by pointing a camera at it.
///
/// This is the way out of AuthGeek onto another device, and it is deliberately as easy as the way
/// in. The window says out loud what is on the screen, because a QR code does not look like a
/// secret and it is exactly as dangerous as one.
///
/// The code is drawn as rectangles rather than turned into an image file, so nothing containing a
/// secret is ever written to disk to make this work.
/// </summary>
internal static class QrWindow
{
    public static Task ShowAsync(Window owner, Account account)
    {
        var window = new Window
        {
            Title = "Add to another device",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#0A0D16"),
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI, Arial")
        };

        var panel = new StackPanel { Margin = new Thickness(22), Spacing = 12 };

        panel.Children.Add(new TextBlock
        {
            Text = account.DisplayName,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        try
        {
            var grid = QrCode.Draw(OtpUri.Format(account));
            panel.Children.Add(Render(grid));
        }
        catch (Exception ex)
        {
            Log.Write("QR: " + ex);
            panel.Children.Add(new TextBlock
            {
                Text = "That account could not be turned into a QR code: " + ex.Message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush.Parse("#E8B45A")
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = "Point your phone's authenticator app at this. Anyone else who photographs it "
                   + "gets the same working access, so close it when you are done.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse("#7C8699"),
            FontSize = 12,
            TextAlignment = TextAlignment.Center
        });

        var close = new Button
        {
            Content = "Close",
            Padding = new Thickness(14, 8),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        close.Click += (_, _) => window.Close();
        panel.Children.Add(close);

        window.Content = panel;
        return window.ShowDialog(owner);
    }

    /// <summary>
    /// Draws the grid onto a white plate. The quiet border is not decoration: a QR code with no
    /// margin around it is one a camera struggles to find.
    /// </summary>
    private static Control Render(bool[,] grid, int cell = 8)
    {
        var cells = grid.GetLength(0);
        var canvas = new Canvas
        {
            Width = cells * cell,
            Height = cells * cell,
            Background = Brushes.White
        };

        var black = Brushes.Black;

        for (var y = 0; y < cells; y++)
        for (var x = 0; x < cells; x++)
        {
            if (!grid[x, y]) continue;

            var square = new Rectangle { Width = cell, Height = cell, Fill = black };
            Canvas.SetLeft(square, x * cell);
            Canvas.SetTop(square, y * cell);
            canvas.Children.Add(square);
        }

        return new Border
        {
            Background = Brushes.White,
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = canvas
        };
    }
}
