using PhoneFolder.Desktop.Models;
using PhoneFolder.Desktop.Services;
using System.Windows;

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
}
