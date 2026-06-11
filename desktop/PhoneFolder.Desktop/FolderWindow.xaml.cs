using Microsoft.Win32;
using PhoneFolder.Desktop.Models;
using PhoneFolder.Desktop.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PhoneFolder.Desktop;

public partial class FolderWindow : Window
{
    private const string RemoteItemsFormat = "PhoneTransfer.RemoteItems";
    private readonly RemoteClient _client;
    private readonly ObservableCollection<RemoteItem> _items = [];
    private readonly Stack<IReadOnlyList<FolderLocation>> _history = [];
    private IReadOnlyList<FolderLocation> _path;

    public FolderWindow(
        RemoteClient client,
        IReadOnlyList<(string Id, string Name)> path)
    {
        InitializeComponent();
        _client = client.CreateSibling();
        _path = path.Select(item => new FolderLocation(item.Id, item.Name)).ToArray();
        FilesGrid.ItemsSource = _items;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private FolderLocation Current => _path[^1];

    private async Task RefreshAsync()
    {
        try
        {
            var children = await _client.GetChildrenAsync(Current.Id);
            _items.Clear();
            foreach (var item in children
                         .OrderByDescending(item => item.IsDirectory)
                         .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                _items.Add(item);
            }
            PathText.Text = string.Join(" > ", _path.Select(item => item.Name));
            Title = $"{Current.Name} - Phone Transfer";
            BackButton.IsEnabled = _history.Count > 0;
            UpButton.IsEnabled = _path.Count > 1;
            StatusText.Text = $"{children.Count} item(s)";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private async Task OpenAsync(RemoteItem item)
    {
        if (!item.IsDirectory)
        {
            var settings = AppSettingsStore.Load();
            if (settings.AlwaysOpenInDefaultApplication && (item.IsVideo || item.IsAudio))
            {
                DefaultMediaSessionManager.Open(_client, item);
                return;
            }
            MessageBox.Show(
                this,
                "Open media and documents from the main window to use its viewer and default-app settings.",
                "Phone Transfer");
            return;
        }

        _history.Push(_path);
        _path = _path.Append(new FolderLocation(item.Id, item.Name)).ToArray();
        await RefreshAsync();
    }

    private async void Files_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FilesGrid.SelectedItem is RemoteItem item)
        {
            await OpenAsync(item);
        }
    }

    private async void OpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (FilesGrid.SelectedItem is RemoteItem item)
        {
            await OpenAsync(item);
        }
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_history.Count == 0)
        {
            return;
        }
        _path = _history.Pop();
        await RefreshAsync();
    }

    private async void UpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_path.Count <= 1)
        {
            return;
        }
        _history.Push(_path);
        _path = _path.Take(_path.Count - 1).ToArray();
        await RefreshAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await RefreshAsync();

    private void CopyButton_Click(object sender, RoutedEventArgs e) =>
        SetClipboard(cut: false);

    private void CutButton_Click(object sender, RoutedEventArgs e) =>
        SetClipboard(cut: true);

    private void SetClipboard(bool cut)
    {
        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            ShowError("Check or select one or more items first.");
            return;
        }
        RemoteClipboard.Set(selected, cut);
        StatusText.Text = $"{(cut ? "Cut" : "Copied")} {selected.Count} item(s).";
    }

    private async void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!RemoteClipboard.HasItems)
        {
            ShowError("The Phone Transfer clipboard is empty.");
            return;
        }
        try
        {
            await RemoteClipboard.PasteAsync(_client, Current.Id);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedItems();
        if (selected.Count == 0
            || MessageBox.Show(
                this,
                $"Delete {selected.Count} selected item(s) from the phone?",
                "Phone Transfer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        try
        {
            foreach (var item in selected)
            {
                await _client.DeleteAsync(item.Id);
            }
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
        {
            QueueUploads(dialog.FileNames);
        }
    }

    private void QueueUploads(IEnumerable<string> paths)
    {
        var destinationId = Current.Id;
        foreach (var path in paths.Where(item => File.Exists(item) || Directory.Exists(item)))
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            var size = PathSize(path);
            TransferManager.Instance.Enqueue(
                _client,
                name,
                "Upload",
                size,
                async (client, progress, cancellationToken) =>
                {
                    if (Directory.Exists(path))
                    {
                        await client.UploadDirectoryAsync(
                            destinationId,
                            path,
                            (_, value) => progress(value),
                            cancellationToken);
                    }
                    else
                    {
                        await client.UploadAsync(
                            destinationId,
                            path,
                            progress,
                            cancellationToken);
                    }
                },
                completed: () =>
                {
                    if (Current.Id == destinationId)
                    {
                        _ = RefreshAsync();
                    }
                });
        }
        ShowTransfers();
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            ShowError("Check or select one or more items first.");
            return;
        }
        var dialog = new OpenFolderDialog { Multiselect = false };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        foreach (var item in selected)
        {
            TransferManager.Instance.Enqueue(
                _client,
                item.Name,
                "Download",
                item.Size,
                async (client, progress, cancellationToken) =>
                {
                    await client.DownloadSelectionAsync(
                        [item],
                        dialog.FolderName,
                        (_, value, _) => progress(value),
                        cancellationToken);
                });
        }
        ShowTransfers();
    }

    private void OpenFolderWindowButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = SelectedItems().FirstOrDefault(item => item.IsDirectory);
        var path = folder is null
            ? _path.Select(item => (item.Id, item.Name)).ToArray()
            : _path.Append(new FolderLocation(folder.Id, folder.Name))
                .Select(item => (item.Id, item.Name))
                .ToArray();
        new FolderWindow(_client, path) { Owner = Owner ?? this }.Show();
    }

    private void TransfersButton_Click(object sender, RoutedEventArgs e) => ShowTransfers();

    private void ShowTransfers()
    {
        var window = Application.Current.Windows.OfType<TransferWindow>().FirstOrDefault();
        if (window is null)
        {
            window = new TransferWindow { Owner = Owner ?? this };
            window.Show();
        }
        else
        {
            window.Activate();
        }
    }

    private void Files_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            return;
        }
        var data = new DataObject();
        data.SetData(RemoteItemsFormat, selected.ToArray());
        DragDrop.DoDragDrop(FilesGrid, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private void Files_DragOver(object sender, DragEventArgs e)
    {
        var accepted = e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetDataPresent(RemoteItemsFormat);
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        DropHint.Visibility = accepted ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private async void Files_Drop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        if (e.Data.GetDataPresent(DataFormats.FileDrop)
            && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            QueueUploads(paths);
            return;
        }
        if (e.Data.GetDataPresent(RemoteItemsFormat)
            && e.Data.GetData(RemoteItemsFormat) is RemoteItem[] items)
        {
            RemoteClipboard.Set(items, cut: false);
            await RemoteClipboard.PasteAsync(_client, Current.Id);
            await RefreshAsync();
        }
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && e.Key == Key.Left))
        {
            BackButton_Click(sender, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.C)
        {
            SetClipboard(cut: false);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.X)
        {
            SetClipboard(cut: true);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.V)
        {
            PasteButton_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            await RefreshAsync();
            e.Handled = true;
        }
    }

    private IReadOnlyList<RemoteItem> SelectedItems()
    {
        var checkedItems = _items.Where(item => item.IsChecked).ToArray();
        return checkedItems.Length > 0
            ? checkedItems
            : FilesGrid.SelectedItems.Cast<RemoteItem>().ToArray();
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "Phone Transfer", MessageBoxButton.OK, MessageBoxImage.Error);

    private static long PathSize(string path)
    {
        if (File.Exists(path))
        {
            return new FileInfo(path).Length;
        }
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }
        catch
        {
            return 0;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _client.Dispose();
        base.OnClosed(e);
    }

    private sealed record FolderLocation(string Id, string Name);
}
