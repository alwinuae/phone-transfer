using PhoneFolder.Desktop.Services;
using System.Windows;

namespace PhoneFolder.Desktop;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DefaultApplicationCheckBox.IsChecked =
            AppSettingsStore.Load().AlwaysOpenInDefaultApplication;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        AppSettingsStore.Save(new AppSettings(
            DefaultApplicationCheckBox.IsChecked == true));
        DialogResult = true;
    }
}
