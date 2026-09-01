using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using AuthGeek.ViewModels;

namespace AuthGeek.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void OnChangePassword(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        if (this.GetVisualRoot() is not Window window) return;

        var current = await Confirm.AskPasswordAsync(window,
            "Current master password",
            "Enter the master password you use now.",
            "Next", twice: false);

        if (current is null) return;

        var next = await Confirm.AskPasswordAsync(window,
            "New master password",
            "Choose the new one. At least eight characters, and something you will not lose: " +
            "there is no way to reset it.",
            "Change it", twice: true);

        if (next is null) return;

        var outcome = vm.ChangePassword(current, next);

        var box = this.FindControl<TextBlock>("PasswordOutcome")!;
        box.Text = outcome;
        box.IsVisible = true;
    }
}
