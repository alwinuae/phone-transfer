using PhoneFolder.Desktop.Services;
using System.Windows;

namespace PhoneFolder.Desktop;

public partial class TrustedDevicesWindow : Window
{
    public TrustedDevicesWindow()
    {
        InitializeComponent();
        Refresh();
    }

    private void Refresh()
    {
        ProfilesGrid.ItemsSource = ConnectionProfileStore.LoadAll()
            .OrderByDescending(profile => profile.LastConnectedAt)
            .Select(profile => new TrustedPhoneRow(profile))
            .ToList();
    }

    private void RemoveSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesGrid.SelectedItem is not TrustedPhoneRow selected)
        {
            MessageBox.Show(this, "Select a trusted phone first.", Title);
            return;
        }
        ConnectionProfileStore.Delete(selected.Profile.CertificateFingerprint);
        Refresh();
    }

    private void RemoveAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                "Remove every trusted phone from this PC?",
                Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        ConnectionProfileStore.Delete();
        Refresh();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record TrustedPhoneRow(RememberedConnection Profile)
    {
        public string DeviceName => Profile.DeviceName;
        public string Host => Profile.Host;
        public string LastConnectedLabel => Profile.LastConnectedAt == default
            ? "Previously"
            : Profile.LastConnectedAt.LocalDateTime.ToString("g");
    }
}
