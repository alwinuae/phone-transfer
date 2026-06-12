using PhoneFolder.Desktop.Models;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace PhoneFolder.Desktop.Services;

public static class RemoteFileLauncher
{
    public static void Open(
        Window owner,
        RemoteClient client,
        RemoteItem item,
        IReadOnlyList<RemoteItem> folderMedia,
        Action<string> reportStatus,
        Action showTransfers)
    {
        if (item.IsDirectory)
        {
            return;
        }

        if (AppSettingsStore.Load().AlwaysOpenInDefaultApplication)
        {
            if (item.IsVideo || item.IsAudio)
            {
                DefaultMediaSessionManager.Open(client, item);
                reportStatus($"Streaming {item.Name} in the Windows default application.");
                return;
            }

            QueueDownloadAndOpen(client, item, reportStatus, showTransfers);
            return;
        }

        if (item.IsMedia)
        {
            WindowCoordinator.Instance.ShowIndependent(
                new MediaPreviewWindow(client, item, folderMedia));
            return;
        }

        QueueDownloadAndOpen(client, item, reportStatus, showTransfers);
    }

    private static void QueueDownloadAndOpen(
        RemoteClient client,
        RemoteItem item,
        Action<string> reportStatus,
        Action showTransfers)
    {
        var cacheDirectory = Path.Combine(
            Path.GetTempPath(),
            "Phone Transfer",
            "Opened Files",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{client.ConnectionKey}\n{item.Id}\n{item.ModifiedAt}\n{item.Size}"))));
        var destination = Path.Combine(
            cacheDirectory,
            FileNameSanitizer.Sanitize(item.Name));

        if (File.Exists(destination)
            && new FileInfo(destination).Length == item.Size)
        {
            StartDefaultApplication(destination);
            reportStatus($"Opened {item.Name} in the Windows default application.");
            return;
        }

        TransferManager.Instance.Enqueue(
            client,
            item.Name,
            "Open",
            item.Size,
            async (transferClient, progress, cancellationToken) =>
            {
                await transferClient.DownloadAsync(
                    item,
                    destination,
                    progress,
                    cancellationToken);
            },
            completed: () =>
            {
                StartDefaultApplication(destination);
                reportStatus($"Opened {item.Name} in the Windows default application.");
            },
            location: "Windows default application");
        showTransfers();
        reportStatus($"Queued {item.Name} for opening.");
    }

    private static void StartDefaultApplication(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
