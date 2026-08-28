using PhoneFolder.Desktop.Models;
using PhoneFolder.Desktop.Services;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace PhoneFolder.Desktop;

public partial class TransferWindow : Window
{
    public TransferWindow()
    {
        InitializeComponent();
        JobsGrid.ItemsSource = TransferManager.Instance.Jobs;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (JobsGrid.SelectedItem is TransferJob job)
        {
            job.Cancel();
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e) =>
        TransferManager.Instance.ClearCompleted();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TransferJob { LocalPath: { } path } })
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Could not open file",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowInFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TransferJob { LocalPath: { } path } })
        {
            return;
        }

        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Could not show file",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
