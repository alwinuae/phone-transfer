using PhoneFolder.Desktop.Services;
using System.Windows;

namespace PhoneFolder.Desktop;

public partial class SettingsWindow : Window
{
    private static readonly ThemeOption[] ThemeOptions =
    [
        new(AppThemeMode.Dark, "Dark"),
        new(AppThemeMode.Light, "Light"),
        new(AppThemeMode.System, "Use Windows setting")
    ];

    public SettingsWindow()
    {
        InitializeComponent();
        var settings = AppSettingsStore.Load();
        DefaultApplicationCheckBox.IsChecked = settings.AlwaysOpenInDefaultApplication;
        ThemeCombo.ItemsSource = ThemeOptions;
        ThemeCombo.SelectedItem = ThemeOptions.FirstOrDefault(option => option.Value == settings.Theme)
            ?? ThemeOptions[0];
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var theme = (ThemeCombo.SelectedItem as ThemeOption)?.Value ?? AppThemeMode.Dark;
        AppSettingsStore.Save(new AppSettings(
            DefaultApplicationCheckBox.IsChecked == true,
            theme));
        ThemeService.Apply(theme);
        DialogResult = true;
    }

    private sealed record ThemeOption(AppThemeMode Value, string DisplayName);
}
