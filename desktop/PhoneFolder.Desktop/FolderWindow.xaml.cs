using Microsoft.Win32;
using PhoneFolder.Desktop.Models;
using PhoneFolder.Desktop.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace PhoneFolder.Desktop;

public partial class FolderWindow : Window
{
    private const string RemoteItemsFormat = "PhoneTransfer.RemoteItems";
    private readonly RemoteClient _client;
    private readonly ObservableCollection<RemoteItem> _items = [];
    private readonly ListCollectionView _itemsView;
    private readonly Stack<IReadOnlyList<FolderLocation>> _history = [];
    private IReadOnlyList<FolderLocation> _path;
    private CancellationTokenSource? _thumbnailCancellation;
    private FileSortField _sortField = FileSortField.Name;
    private bool _sortDescending;
    private FileViewMode _viewMode = FileViewMode.Details;

    public FolderWindow(
        RemoteClient client,
        IReadOnlyList<(string Id, string Name)> path)
    {
        InitializeComponent();
        _client = client.CreateSibling();
        _path = path.Select(item => new FolderLocation(item.Id, item.Name)).ToArray();
        _itemsView = (ListCollectionView)CollectionViewSource.GetDefaultView(_items);
        ApplySort();
        FilesGrid.ItemsSource = _itemsView;
        FilesList.ItemsSource = _itemsView;
        ThumbnailList.ItemsSource = _itemsView;
        RemoteClipboard.Changed += RemoteClipboard_Changed;
        Activated += (_, _) => UpdateActionState();
        Loaded += async (_, _) => await RefreshAsync();
        UpdateActionState();
    }

    private FolderLocation Current => _path[^1];

    private RemoteItem RootItem => new()
    {
        Id = _path[0].Id,
        Name = _path[0].Name,
        IsDirectory = true,
        CanWrite = true
    };

    private async Task RefreshAsync()
    {
        try
        {
            _thumbnailCancellation?.Cancel();
            var children = await _client.GetChildrenAsync(Current.Id);
            _items.Clear();
            foreach (var item in children
                         .OrderByDescending(item => item.IsDirectory))
            {
                item.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(RemoteItem.IsChecked))
                    {
                        UpdateActionState();
                    }
                };
                _items.Add(item);
            }
            _itemsView.Refresh();
            PathText.Text = string.Join(" > ", _path.Select(item => item.Name));
            Title = $"{Current.Name} - Phone Transfer";
            BackButton.IsEnabled = _history.Count > 0;
            UpButton.IsEnabled = _path.Count > 1;
            var totalSize = children.Where(item => !item.IsDirectory)
                .Sum(item => Math.Max(0, item.Size));
            StatusText.Text = totalSize > 0
                ? $"{children.Count} item(s) | {FormatSize(totalSize)}"
                : $"{children.Count} item(s)";
            UpdateActionState();
            await LoadThumbnailsIfNeededAsync();
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
            try
            {
                RemoteFileLauncher.Open(
                    this,
                    _client,
                    item,
                    _items.Where(candidate => candidate.IsMedia).ToArray(),
                    status => StatusText.Text = status,
                    ShowTransfers);
            }
            catch (Exception exception)
            {
                ShowError(exception.Message);
            }
            return;
        }

        _history.Push(_path);
        _path = _path.Append(new FolderLocation(item.Id, item.Name)).ToArray();
        await RefreshAsync();
    }

    private async void Files_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedItem() is { } item)
        {
            await OpenAsync(item);
        }
    }

    private async void OpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem() is { } item)
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

    private void Files_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateActionState();

    private void ItemCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: RemoteItem item } checkBox)
        {
            item.IsChecked = checkBox.IsChecked == true;
        }
        UpdateActionState();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e) =>
        SetClipboard(cut: false);

    private void CutButton_Click(object sender, RoutedEventArgs e) =>
        SetClipboard(cut: true);

    private void SetClipboard(bool cut)
    {
        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            return;
        }
        RemoteClipboard.Set(_client, selected, cut);
        StatusText.Text = $"{(cut ? "Cut" : "Copied")} {selected.Count} item(s).";
        UpdateActionState();
    }

    private async void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!RemoteClipboard.HasItems)
        {
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

    private async void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItems() is not { Count: 1 } selected)
        {
            return;
        }

        var item = selected[0];
        var name = PromptWindow.Show(this, "Rename", "New name", item.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == item.Name)
        {
            return;
        }

        try
        {
            await _client.RenameAsync(item.Id, name.Trim());
            StatusText.Text = $"Renamed {item.Name}.";
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private async void CopyToButton_Click(object sender, RoutedEventArgs e) =>
        await CopyOrMoveAsync(move: false);

    private async void MoveToButton_Click(object sender, RoutedEventArgs e) =>
        await CopyOrMoveAsync(move: true);

    private async Task CopyOrMoveAsync(bool move)
    {
        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            return;
        }

        var action = move ? "Move" : "Copy";
        var destination = MoveWindow.Choose(
            this,
            _client,
            RootItem,
            selected.Where(item => item.IsDirectory).Select(item => item.Id),
            action);
        if (destination is null)
        {
            return;
        }

        try
        {
            foreach (var item in selected)
            {
                if (move)
                {
                    await _client.MoveAsync(item.Id, destination.Id);
                }
                else
                {
                    await _client.CopyAsync(item.Id, destination.Id);
                }
            }
            StatusText.Text =
                $"{(move ? "Moved" : "Copied")} {selected.Count} item(s) to {destination.Name}.";
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
                },
                location: Current.Name);
        }
        ShowTransfers();
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedItems();
        if (selected.Count == 0)
        {
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
                },
                location: dialog.FolderName);
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
        WindowCoordinator.Instance.ShowIndependent(new FolderWindow(_client, path));
    }

    private void TransfersButton_Click(object sender, RoutedEventArgs e) => ShowTransfers();

    private void ShowTransfers()
    {
        WindowCoordinator.Instance.ShowSingleton(() => new TransferWindow());
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
        data.SetData(
            RemoteItemsFormat,
            new RemoteDragPayload(
                _client.ConnectionKey,
                _client.DeviceName,
                selected.ToArray()));
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private void Files_DragOver(object sender, DragEventArgs e)
    {
        var accepted = e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetDataPresent(RemoteItemsFormat);
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        DropHint.Visibility = accepted ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Files_DragLeave(object sender, DragEventArgs e) =>
        DropHint.Visibility = Visibility.Collapsed;

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
            && e.Data.GetData(RemoteItemsFormat) is RemoteDragPayload payload)
        {
            try
            {
                RemoteClipboard.Set(
                    payload.ConnectionKey,
                    payload.DeviceName,
                    payload.Items,
                    cut: false);
                await RemoteClipboard.PasteAsync(_client, Current.Id);
                await RefreshAsync();
            }
            catch (Exception exception)
            {
                ShowError(exception.Message);
            }
        }
    }

    private void ViewModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModeButton.ContextMenu is null)
        {
            return;
        }
        ViewModeButton.ContextMenu.PlacementTarget = ViewModeButton;
        ViewModeButton.ContextMenu.Placement = PlacementMode.Bottom;
        ViewModeButton.ContextMenu.IsOpen = true;
    }

    private async void ViewDetails_Click(object sender, RoutedEventArgs e) =>
        await SetViewModeAsync(FileViewMode.Details);

    private async void ViewList_Click(object sender, RoutedEventArgs e) =>
        await SetViewModeAsync(FileViewMode.List);

    private async void ViewTiles_Click(object sender, RoutedEventArgs e) =>
        await SetViewModeAsync(FileViewMode.Tiles);

    private async void ViewSmallIcons_Click(object sender, RoutedEventArgs e) =>
        await SetViewModeAsync(FileViewMode.SmallIcons);

    private async void ViewMediumIcons_Click(object sender, RoutedEventArgs e) =>
        await SetViewModeAsync(FileViewMode.MediumIcons);

    private async void ViewLargeIcons_Click(object sender, RoutedEventArgs e) =>
        await SetViewModeAsync(FileViewMode.LargeIcons);

    private async Task SetViewModeAsync(FileViewMode mode)
    {
        _viewMode = mode;
        FilesGrid.Visibility = mode == FileViewMode.Details ? Visibility.Visible : Visibility.Collapsed;
        FilesList.Visibility = mode == FileViewMode.List ? Visibility.Visible : Visibility.Collapsed;
        ThumbnailList.Visibility =
            mode is not FileViewMode.Details and not FileViewMode.List
                ? Visibility.Visible
                : Visibility.Collapsed;
        ThumbnailList.ItemTemplate = (DataTemplate)FindResource(mode switch
        {
            FileViewMode.Tiles => "FileTileTemplate",
            FileViewMode.SmallIcons => "FileSmallIconTemplate",
            FileViewMode.MediumIcons => "FileMediumIconTemplate",
            _ => "FileLargeIconTemplate"
        });
        ViewModeButton.Content = $"View: {ViewModeLabel(mode)}";
        UpdateActionState();
        await LoadThumbnailsIfNeededAsync();
    }

    private void SortModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (SortModeButton.ContextMenu is null)
        {
            return;
        }
        SortModeButton.ContextMenu.PlacementTarget = SortModeButton;
        SortModeButton.ContextMenu.Placement = PlacementMode.Bottom;
        SortModeButton.ContextMenu.IsOpen = true;
    }

    private void SortName_Click(object sender, RoutedEventArgs e) =>
        SetSort(FileSortField.Name);

    private void SortModified_Click(object sender, RoutedEventArgs e) =>
        SetSort(FileSortField.Modified);

    private void SortType_Click(object sender, RoutedEventArgs e) =>
        SetSort(FileSortField.Type);

    private void SortSize_Click(object sender, RoutedEventArgs e) =>
        SetSort(FileSortField.Size);

    private void SortAscending_Click(object sender, RoutedEventArgs e)
    {
        _sortDescending = false;
        ApplySort();
    }

    private void SortDescending_Click(object sender, RoutedEventArgs e)
    {
        _sortDescending = true;
        ApplySort();
    }

    private void SetSort(FileSortField field)
    {
        _sortField = field;
        ApplySort();
    }

    private void ApplySort()
    {
        _itemsView.CustomSort = new RemoteItemComparer(_sortField, _sortDescending);
        SortModeButton.Content =
            $"Sort: {SortFieldLabel(_sortField)} {(_sortDescending ? "\u2193" : "\u2191")}";
        _itemsView.Refresh();
    }

    private static string ViewModeLabel(FileViewMode mode) => mode switch
    {
        FileViewMode.SmallIcons => "Small icons",
        FileViewMode.MediumIcons => "Medium icons",
        FileViewMode.LargeIcons => "Large icons",
        _ => mode.ToString()
    };

    private static string SortFieldLabel(FileSortField field) => field switch
    {
        FileSortField.Modified => "Date modified",
        _ => field.ToString()
    };

    private async Task LoadThumbnailsIfNeededAsync()
    {
        _thumbnailCancellation?.Cancel();
        if (ThumbnailList.Visibility != Visibility.Visible)
        {
            return;
        }

        _thumbnailCancellation = new CancellationTokenSource();
        var cancellationToken = _thumbnailCancellation.Token;
        using var throttle = new SemaphoreSlim(4);
        var tasks = _items
            .Where(item => !item.IsDirectory
                && item.Thumbnail is null
                && item.SupportsThumbnail)
            .Select(async item =>
            {
                await throttle.WaitAsync(cancellationToken);
                try
                {
                    var bytes = await _client.GetThumbnailAsync(item.Id, 256, cancellationToken);
                    if (bytes is null || bytes.Length == 0)
                    {
                        return;
                    }
                    using var stream = new MemoryStream(bytes);
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    item.Thumbnail = image;
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                    // Thumbnail failures should not interrupt folder browsing.
                }
                finally
                {
                    throttle.Release();
                }
            })
            .ToArray();
        await Task.WhenAll(tasks);
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var isViewShortcut = control && key is Key.D1 or Key.D2 or Key.D3 or Key.D4 or Key.D5 or Key.D6;
        if (Keyboard.FocusedElement is TextBox && key != Key.Escape && !isViewShortcut)
        {
            return;
        }

        if (key == Key.Escape || (alt && key == Key.Left))
        {
            BackButton_Click(sender, e);
            e.Handled = true;
        }
        else if (alt && key == Key.Up)
        {
            UpButton_Click(sender, e);
            e.Handled = true;
        }
        else if (control && key == Key.C && SelectedItems().Count > 0)
        {
            SetClipboard(cut: false);
            e.Handled = true;
        }
        else if (control && key == Key.X && SelectedItems().Count > 0)
        {
            SetClipboard(cut: true);
            e.Handled = true;
        }
        else if (control && key == Key.V && RemoteClipboard.HasItems)
        {
            PasteButton_Click(sender, e);
            e.Handled = true;
        }
        else if (control && key == Key.A)
        {
            SelectAllVisibleItems();
            e.Handled = true;
        }
        else if (control && key == Key.D1)
        {
            await SetViewModeAsync(FileViewMode.Details);
            e.Handled = true;
        }
        else if (control && key == Key.D2)
        {
            await SetViewModeAsync(FileViewMode.List);
            e.Handled = true;
        }
        else if (control && key == Key.D3)
        {
            await SetViewModeAsync(FileViewMode.Tiles);
            e.Handled = true;
        }
        else if (control && key == Key.D4)
        {
            await SetViewModeAsync(FileViewMode.SmallIcons);
            e.Handled = true;
        }
        else if (control && key == Key.D5)
        {
            await SetViewModeAsync(FileViewMode.MediumIcons);
            e.Handled = true;
        }
        else if (control && key == Key.D6)
        {
            await SetViewModeAsync(FileViewMode.LargeIcons);
            e.Handled = true;
        }
        else if (key == Key.Delete && SelectedItems().Count > 0)
        {
            DeleteButton_Click(sender, e);
            e.Handled = true;
        }
        else if (key == Key.Enter && SelectedItem() is { } selected)
        {
            await OpenAsync(selected);
            e.Handled = true;
        }
        else if (key == Key.F5)
        {
            await RefreshAsync();
            e.Handled = true;
        }
    }

    private void SelectAllVisibleItems()
    {
        foreach (var item in _items)
        {
            item.IsChecked = true;
        }
        if (FilesGrid.Visibility == Visibility.Visible)
        {
            FilesGrid.SelectAll();
        }
        else if (FilesList.Visibility == Visibility.Visible)
        {
            FilesList.SelectAll();
        }
        else
        {
            ThumbnailList.SelectAll();
        }
    }

    private IReadOnlyList<RemoteItem> SelectedItems()
    {
        var checkedItems = _items.Where(item => item.IsChecked).ToArray();
        if (checkedItems.Length > 0)
        {
            return checkedItems;
        }
        if (FilesGrid.Visibility == Visibility.Visible)
        {
            return FilesGrid.SelectedItems.Cast<RemoteItem>().ToArray();
        }
        if (FilesList.Visibility == Visibility.Visible)
        {
            return FilesList.SelectedItems.Cast<RemoteItem>().ToArray();
        }
        return ThumbnailList.SelectedItems.Cast<RemoteItem>().ToArray();
    }

    private RemoteItem? SelectedItem()
    {
        var checkedItem = _items.FirstOrDefault(item => item.IsChecked);
        if (checkedItem is not null)
        {
            return checkedItem;
        }
        return FilesGrid.Visibility == Visibility.Visible
            ? FilesGrid.SelectedItem as RemoteItem
            : FilesList.Visibility == Visibility.Visible
                ? FilesList.SelectedItem as RemoteItem
                : ThumbnailList.SelectedItem as RemoteItem;
    }

    private void RemoteClipboard_Changed(object? sender, EventArgs e) =>
        Dispatcher.Invoke(UpdateActionState);

    private void UpdateActionState()
    {
        if (!IsInitialized)
        {
            return;
        }

        var selectedCount = SelectedItems().Count;
        var hasSelection = selectedCount > 0;
        var hasSingleSelection = selectedCount == 1;
        DownloadButton.IsEnabled = hasSelection;
        CopyButton.IsEnabled = hasSelection;
        CutButton.IsEnabled = hasSelection;
        PasteButton.IsEnabled = RemoteClipboard.HasItems;
        CopyToButton.IsEnabled = hasSelection;
        MoveToButton.IsEnabled = hasSelection;
        RenameButton.IsEnabled = hasSingleSelection;
        DeleteButton.IsEnabled = hasSelection;

        OpenMenuItem.IsEnabled = hasSingleSelection;
        CopyMenuItem.IsEnabled = hasSelection;
        CutMenuItem.IsEnabled = hasSelection;
        PasteMenuItem.IsEnabled = RemoteClipboard.HasItems;
        CopyToMenuItem.IsEnabled = hasSelection;
        MoveToMenuItem.IsEnabled = hasSelection;
        RenameMenuItem.IsEnabled = hasSingleSelection;
        DeleteMenuItem.IsEnabled = hasSelection;
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

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }

    protected override void OnClosed(EventArgs e)
    {
        RemoteClipboard.Changed -= RemoteClipboard_Changed;
        _thumbnailCancellation?.Cancel();
        _client.Dispose();
        base.OnClosed(e);
    }

    private sealed record FolderLocation(string Id, string Name);
}
