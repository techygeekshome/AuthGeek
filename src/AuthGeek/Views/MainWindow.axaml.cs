using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AuthGeek.ViewModels;
using TechyGeeksHome.Common;

namespace AuthGeek.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ShowLockState();

        // Any keyboard or pointer activity counts as being used, which is what keeps the
        // auto-lock from firing while somebody is reading a code off the screen.
        AddHandler(KeyDownEvent, (_, _) => Vm?.Touch(), RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, (_, _) => Vm?.Touch(), RoutingStrategies.Tunnel);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private ShellViewModel? Vm => DataContext as ShellViewModel;

    protected override void OnClosed(EventArgs e)
    {
        Vm?.Dispose();
        base.OnClosed(e);
    }

    // ------------------------------------------------------------------ unlocking

    /// <summary>
    /// The unlock panel doubles as the first-run panel. Nothing about creating a vault needs a
    /// separate screen: the only decision is what the password will be, and it is the same box.
    /// </summary>
    private void ShowLockState()
    {
        if (Vm is null) return;

        var first = Vm.IsFirstRun;

        this.FindControl<TextBlock>("LockedBlurb")!.Text = first
            ? "There is no vault on this computer yet. Choose a master password and AuthGeek will make one. "
              + "It never leaves this machine and it cannot be reset, so pick something you will not lose."
            : "Enter your master password to see your codes.";

        this.FindControl<TextBox>("ConfirmBox")!.IsVisible = first;
        this.FindControl<Button>("UnlockButton")!.Content = first ? "Create the vault" : "Unlock";

        this.FindControl<TextBox>("PasswordBox")!.Focus();
    }

    private void OnPasswordKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryUnlock();
    }

    private void OnUnlock(object? sender, RoutedEventArgs e) => TryUnlock();

    private void TryUnlock()
    {
        if (Vm is null) return;

        var password = this.FindControl<TextBox>("PasswordBox")!;
        var confirm = this.FindControl<TextBox>("ConfirmBox")!;

        if (Vm.IsFirstRun && (password.Text ?? "") != (confirm.Text ?? ""))
        {
            Vm.UnlockMessage = "Those two do not match.";
            return;
        }

        if (Vm.Unlock(password.Text ?? ""))
        {
            password.Text = "";
            confirm.Text = "";
        }
        else
        {
            password.SelectAll();
            password.Focus();
        }

        ShowLockState();
    }

    // ------------------------------------------------------------------ sidebar foot

    private void OnAbout(object? sender, RoutedEventArgs e)
        => new AboutWindow(AppFacts.Info).ShowDialog(this);

    private async void OnCheckUpdates(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b) { b.IsEnabled = false; b.Content = "Checking…"; }

        try
        {
            var result = await UpdateChecker.CheckAsync(AppFacts.Info);
            await Notice.ShowAsync(this, "Check for updates", result.Message,
                result.Status == UpdateStatus.UpdateAvailable ? result.ReleaseUrl : null);
        }
        finally
        {
            if (sender is Button b2) { b2.IsEnabled = true; b2.Content = "Check for updates"; }
        }
    }
}
