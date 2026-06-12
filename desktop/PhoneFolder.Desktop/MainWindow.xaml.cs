using Microsoft.Win32;
using PhoneFolder.Desktop.Models;
using PhoneFolder.Desktop.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace PhoneFolder.Desktop;

public partial class MainWindow : Window
{
    private const string RemoteItemsFormat = "PhoneTransfer.RemoteItems";
    private readonly ObservableCollection<RemoteItem> _items = [];
    private readonly ObservableCollection<DiscoveredDevice> _discoveredDevices = [];
    private readonly ObservableCollection<RememberedConnection> _trustedDevices = [];
    private readonly ObservableCollection<FolderNode> _folderRoots = [];
    private readonly Stack<NavigationEntry> _history = [];
    private readonly DiscoveryService _discoveryService = new();
    private RememberedConnection? _rememberedConnection;
    private RemoteClient? _client;
    private RemoteItem? _rootItem;
    private NavigationEntry? _current;
    private CancellationTokenSource? _thumbnailCancellation;
    private GridLength _folderPaneWidth = new(220);
    private int _busyDepth;
    private TransferWindow? _transferWindow;

    public MainWindow()
    {
        InitializeComponent();
        FilesGrid.ItemsSource = _items;
        FilesList.ItemsSource = _items;
        ThumbnailList.ItemsSource = _items;
        DiscoveredDevicesCombo.ItemsSource = _discoveredDevices;
        TrustedDevicesCombo.ItemsSource = _trustedDevices;
        FolderTree.DataContext = _folderRoots;
        RefreshTrustedDevices(ConnectionProfileStore.Load());
        _rememberedConnection = TrustedDevicesCombo.SelectedItem as RememberedConnection;
        if (_rememberedConnection is not null)
        {
            ApplyRememberedConnection(_rememberedConnection);
            ConnectionStatusText.Text = $"Saved phone: {_rememberedConnection.DeviceName}";
        }
        SetupExpander.IsExpanded = _rememberedConnection is null;
        TransferManager.Instance.Changed += TransferManager_Changed;
        UpdateActionState();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_rememberedConnection is not null)
        {
            await ConnectFromFieldsAsync(automatic: true);
        }
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var isViewShortcut = control && key is Key.D1 or Key.D2 or Key.D3;
        if (Keyboard.FocusedElement is TextBox && key != Key.Escape && !isViewShortcut)
        {
            return;
        }

        if (key == Key.Escape && _current is not null)
        {
            if (_history.Count > 0)
            {
                BackButton_Click(sender, e);
            }
            else
            {
                UpButton_Click(sender, e);
            }
            e.Handled = true;
        }
        else if (alt && key == Key.Left)
        {
            BackButton_Click(sender, e);
            e.Handled = true;
        }
        else if (alt && key == Key.Up)
        {
            UpButton_Click(sender, e);
            e.Handled = true;
        }
        else if (key == Key.F5)
        {
            RefreshButton_Click(sender, e);
            e.Handled = true;
        }
        else if (key == Key.Delete && SelectedItems().Count > 0)
        {
            DeleteButton_Click(sender, e);
            e.Handled = true;
        }
        else if (control && key == Key.A)
        {
            SelectAllVisibleItems();
            e.Handled = true;
        }
        else if (control && key == Key.C)
        {
            CopySelectionButton_Click(sender, e);
            e.Handled = true;
        }
        else if (control && key == Key.X)
        {
            CutSelectionButton_Click(sender, e);
            e.Handled = true;
        }
        else if (control && key == Key.V)
        {
            PasteButton_Click(sender, e);
            e.Handled = true;
        }
        else if (control && key == Key.D1)
        {
            await SetViewModeAsync("Details");
            e.Handled = true;
        }
        else if (control && key == Key.D2)
        {
            await SetViewModeAsync("List");
            e.Handled = true;
        }
        else if (control && key == Key.D3)
        {
            await SetViewModeAsync("Thumbnails");
            e.Handled = true;
        }
        else if (key == Key.Enter && SelectedItem() is { } selected)
        {
            await OpenItemAsync(selected);
            e.Handled = true;
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow { Owner = this };
        if (settings.ShowDialog() == true)
        {
            OperationStatusText.Text = AppSettingsStore.Load().AlwaysOpenInDefaultApplication
                ? "Default-application opening is enabled."
                : "Phone Transfer viewers are enabled for photos, video, and audio.";
        }
    }

    private void TransfersButton_Click(object sender, RoutedEventArgs e) => ShowTransfersWindow();

    private void ShowTransfersWindow()
    {
        if (_transferWindow is null)
        {
            _transferWindow = new TransferWindow { Owner = this };
            _transferWindow.Closed += (_, _) => _transferWindow = null;
            _transferWindow.Show();
        }
        else
        {
            _transferWindow.Activate();
        }
    }

    private void TransferManager_Changed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var active = TransferManager.Instance.ActiveCount;
            TransfersButton.Content = active > 0 ? $"Transfers ({active})" : "Transfers";
        });
    }

    private void RefreshTrustedDevices(RememberedConnection? selected)
    {
        var selectedFingerprint = NormalizeFingerprint(selected?.CertificateFingerprint);
        _trustedDevices.Clear();
        foreach (var profile in ConnectionProfileStore.LoadAll()
                     .Where(profile => profile.IsEnabled)
                     .OrderByDescending(profile => profile.LastConnectedAt))
        {
            _trustedDevices.Add(profile);
        }

        TrustedDevicesCombo.SelectedItem = _trustedDevices.FirstOrDefault(profile =>
            NormalizeFingerprint(profile.CertificateFingerprint) == selectedFingerprint)
            ?? _trustedDevices.FirstOrDefault();
        _rememberedConnection = TrustedDevicesCombo.SelectedItem as RememberedConnection;
    }

    private void ApplyRememberedConnection(RememberedConnection profile)
    {
        _rememberedConnection = profile;
        HostTextBox.Text = profile.Host;
        PortTextBox.Text = profile.Port.ToString();
        TokenTextBox.Text = profile.Token;
        RememberDeviceCheckBox.IsChecked = true;
    }

    private void TrustedDevicesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TrustedDevicesCombo.SelectedItem is RememberedConnection profile)
        {
            ApplyRememberedConnection(profile);
            if (_busyDepth == 0)
            {
                OperationStatusText.Text = $"Ready to switch to {profile.DeviceName}.";
            }
        }
    }

    private async void SwitchDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        if (TrustedDevicesCombo.SelectedItem is not RememberedConnection profile)
        {
            ShowError("Select a trusted phone first.");
            return;
        }

        ApplyRememberedConnection(profile);
        await ConnectFromFieldsAsync(automatic: false);
    }

    private async void DiscoverButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Looking for phones on this Wi-Fi network...", async () =>
        {
            _discoveredDevices.Clear();
            foreach (var device in await _discoveryService.DiscoverAsync(TimeSpan.FromSeconds(3)))
            {
                _discoveredDevices.Add(device);
            }

            OperationStatusText.Text = _discoveredDevices.Count == 0
                ? "No phones found. You can still enter the phone address manually."
                : $"Found {_discoveredDevices.Count} phone(s).";

            if (_discoveredDevices.Count == 1)
            {
                DiscoveredDevicesCombo.SelectedIndex = 0;
            }
        });
    }

    private async void ConnectDiscoveredButton_Click(object sender, RoutedEventArgs e)
    {
        if (DiscoveredDevicesCombo.SelectedItem is not DiscoveredDevice)
        {
            ShowError("Find and select a phone on router Wi-Fi first.");
            return;
        }
        await ConnectFromFieldsAsync(automatic: false);
    }

    private void OpenHotspotSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        HotspotService.OpenWindowsSettings();
        OperationStatusText.Text =
            "Turn on Mobile Hotspot, connect the phone to it, then click Find phone on PC hotspot.";
    }

    private async void HotspotButton_Click(object sender, RoutedEventArgs e)
    {
        var hotspot = HotspotService.GetStatus();
        if (!hotspot.Active)
        {
            OperationStatusText.Text =
                "PC hotspot is off. Use Open hotspot settings, then retry.";
            MessageBox.Show(
                this,
                "Windows Mobile Hotspot is not active.\n\n"
                + "1. Turn on Mobile Hotspot.\n"
                + "2. Connect the Android phone to that hotspot.\n"
                + "3. Start sharing in Phone Transfer on Android.\n"
                + "4. Click Find phone on PC hotspot.",
                "PC Hotspot",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DiscoveredDevice? selectedDevice = null;
        try
        {
            SetBusy(
                true,
                $"Searching the PC hotspot on {hotspot.Address}...");
            _discoveredDevices.Clear();
            var devices = await _discoveryService.DiscoverAsync(
                TimeSpan.FromSeconds(4),
                hotspotOnly: true);
            foreach (var device in devices)
            {
                _discoveredDevices.Add(device);
            }

            selectedDevice = _trustedDevices.Count == 0
                ? devices.FirstOrDefault()
                : devices.FirstOrDefault(device => _trustedDevices.Any(profile =>
                    NormalizeFingerprint(device.CertificateFingerprint)
                    == NormalizeFingerprint(profile.CertificateFingerprint)))
                    ?? devices.FirstOrDefault();
            if (selectedDevice is null)
            {
                OperationStatusText.Text =
                    $"No phone answered on the PC hotspot ({hotspot.Address}). "
                    + "Confirm the phone is connected to this hotspot and sharing is active.";
                return;
            }

            DiscoveredDevicesCombo.SelectedItem = selectedDevice;
            OperationStatusText.Text = $"Found {selectedDevice.Name} directly on the PC hotspot.";
        }
        catch (Exception exception)
        {
            OperationStatusText.Text = "PC hotspot discovery failed";
            ShowError(exception.Message);
            return;
        }
        finally
        {
            SetBusy(false);
        }

        if (selectedDevice is not null && !string.IsNullOrWhiteSpace(TokenTextBox.Text))
        {
            await ConnectFromFieldsAsync(automatic: false);
        }
        else
        {
            OperationStatusText.Text =
                "Phone found on the PC hotspot. Enter its access code once, then click Connect.";
        }
    }

    private void DiscoveredDevicesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DiscoveredDevicesCombo.SelectedItem is not DiscoveredDevice device)
        {
            return;
        }

        HostTextBox.Text = device.Address;
        PortTextBox.Text = device.Port.ToString();
        var trusted = _trustedDevices.FirstOrDefault(profile =>
            NormalizeFingerprint(device.CertificateFingerprint)
            == NormalizeFingerprint(profile.CertificateFingerprint));
        if (trusted is not null)
        {
            TrustedDevicesCombo.SelectedItem = trusted;
            ApplyRememberedConnection(trusted with { Host = device.Address, Port = device.Port });
        }
        UpdateActionState();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        await ConnectFromFieldsAsync(automatic: false);
    }

    private async Task ConnectFromFieldsAsync(bool automatic)
    {
        if (!int.TryParse(PortTextBox.Text, out var port) || port is < 1 or > 65535)
        {
            if (!automatic)
            {
                ShowError("Enter a valid port number.");
            }
            return;
        }

        var profile = _rememberedConnection;
        if (string.IsNullOrWhiteSpace(HostTextBox.Text)
            || (string.IsNullOrWhiteSpace(TokenTextBox.Text)
                && string.IsNullOrWhiteSpace(profile?.TrustedToken)))
        {
            if (!automatic)
            {
                ShowError("Enter the phone address and access code, or select a trusted phone.");
            }
            return;
        }

        var host = HostTextBox.Text.Trim();
        var token = TokenTextBox.Text.Trim();
        var selectedFingerprint = DiscoveredDevicesCombo.SelectedItem is DiscoveredDevice selected
            ? selected.CertificateFingerprint
            : null;
        if (string.IsNullOrWhiteSpace(selectedFingerprint)
            && profile is not null)
        {
            selectedFingerprint = profile.CertificateFingerprint;
        }

        try
        {
            SetBusy(true, automatic ? "Reconnecting to saved phone..." : "Connecting...");
            var endpoint = await FindReachableEndpointAsync(
                host,
                port,
                selectedFingerprint);
            using var candidate = new RemoteClient(
                endpoint.Host,
                endpoint.Port,
                token,
                endpoint.CertificateFingerprint,
                profile?.TrustedToken);
            var info = await candidate.GetInfoAsync();
            var roots = await candidate.GetRootsAsync();

            var trustedToken = profile?.TrustedToken ?? string.Empty;
            var clientId = string.IsNullOrWhiteSpace(profile?.ClientId)
                ? Guid.NewGuid().ToString("N")
                : profile.ClientId;
            if (RememberDeviceCheckBox.IsChecked == true
                && string.IsNullOrWhiteSpace(trustedToken)
                && !string.IsNullOrWhiteSpace(token))
            {
                trustedToken = await candidate.TrustThisPcAsync(
                    clientId,
                    $"{Environment.MachineName}\\{Environment.UserName}");
            }

            _client?.Dispose();
            _client = new RemoteClient(
                endpoint.Host,
                endpoint.Port,
                token,
                info.CertificateFingerprint,
                trustedToken,
                info.Name);
            HostTextBox.Text = endpoint.Host;
            PortTextBox.Text = endpoint.Port.ToString();
            ConnectionStatusText.Text = $"Connected to {info.Name}";
            DeviceDetailsText.Text = $"{HotspotService.ConnectionDescription(endpoint.Host)}"
                + $" | HTTPS | Protocol {info.ProtocolVersion}\n"
                + $"Certificate SHA-256:\n{info.CertificateFingerprint}";
            _history.Clear();
            _folderRoots.Clear();

            if (roots.Count == 0)
            {
                _rootItem = null;
                _items.Clear();
                PathText.Text = "No shared folder is available";
                OperationStatusText.Text = "Choose a folder in the Android app.";
                UpdateActionState();
                return;
            }

            _rootItem = roots[0];
            _folderRoots.Add(FolderNode.Create(_rootItem.Id, _rootItem.Name));
            await NavigateAsync(
                new NavigationEntry(
                    _rootItem.Id,
                    _rootItem.Name,
                    [new PathSegment(_rootItem.Id, _rootItem.Name)]),
                addToHistory: false);

            if (RememberDeviceCheckBox.IsChecked == true)
            {
                try
                {
                    _rememberedConnection = new RememberedConnection(
                        endpoint.Host,
                        endpoint.Port,
                        token,
                        info.CertificateFingerprint,
                        info.Name,
                        trustedToken,
                        clientId,
                        DateTimeOffset.UtcNow);
                    ConnectionProfileStore.Save(_rememberedConnection);
                    RefreshTrustedDevices(_rememberedConnection);
                }
                catch (Exception exception)
                {
                    OperationStatusText.Text =
                        $"Connected, but Windows could not remember this phone: {exception.Message}";
                }
            }
            else if (_rememberedConnection is not null)
            {
                ConnectionProfileStore.Delete(_rememberedConnection.CertificateFingerprint);
                _rememberedConnection = null;
                RefreshTrustedDevices(null);
            }
        }
        catch (Exception exception)
        {
            if (automatic)
            {
                ConnectionStatusText.Text = "Saved phone is not currently available";
                OperationStatusText.Text =
                    "Start sharing on Android, then use the matching trusted, Wi-Fi, or hotspot connection button.";
            }
            else
            {
                OperationStatusText.Text = "Connection failed";
                ShowError(exception.Message);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ForgetDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var manager = new TrustedDevicesWindow { Owner = this };
            manager.ShowDialog();
            var selected = _rememberedConnection is null
                ? null
                : ConnectionProfileStore.LoadAll().FirstOrDefault(profile =>
                    NormalizeFingerprint(profile.CertificateFingerprint)
                    == NormalizeFingerprint(_rememberedConnection.CertificateFingerprint));
            RefreshTrustedDevices(selected ?? ConnectionProfileStore.Load());
            if (_rememberedConnection is not null)
            {
                ApplyRememberedConnection(_rememberedConnection);
            }
            else
            {
                RememberDeviceCheckBox.IsChecked = false;
                TokenTextBox.Clear();
            }
            OperationStatusText.Text = "Trusted-phone list updated.";
            UpdateActionState();
        }
        catch (Exception exception)
        {
            ShowError($"Windows could not update trusted phones: {exception.Message}");
        }
    }

    private async Task<ConnectedEndpoint> FindReachableEndpointAsync(
        string host,
        int port,
        string? expectedFingerprint)
    {
        var initial = await NetworkDiagnostics.ProbeAsync(
            host,
            port,
            TimeSpan.FromSeconds(5));
        if (initial.Success)
        {
            return new ConnectedEndpoint(host, port, expectedFingerprint);
        }

        OperationStatusText.Text = "Address did not respond. Checking this Wi-Fi network for the phone...";
        var discovered = await _discoveryService.DiscoverAsync(TimeSpan.FromSeconds(2));
        var normalizedFingerprint = NormalizeFingerprint(expectedFingerprint);
        var replacement = !string.IsNullOrEmpty(normalizedFingerprint)
            ? discovered.FirstOrDefault(device =>
                NormalizeFingerprint(device.CertificateFingerprint) == normalizedFingerprint)
            : discovered.Count == 1
                ? discovered[0]
                : null;

        if (replacement is not null
            && (!replacement.Address.Equals(host, StringComparison.OrdinalIgnoreCase)
                || replacement.Port != port))
        {
            var replacementProbe = await NetworkDiagnostics.ProbeAsync(
                replacement.Address,
                replacement.Port,
                TimeSpan.FromSeconds(5));
            if (replacementProbe.Success)
            {
                HostTextBox.Text = replacement.Address;
                PortTextBox.Text = replacement.Port.ToString();
                OperationStatusText.Text = $"Phone address changed to {replacement.Address}. Connecting...";
                return new ConnectedEndpoint(
                    replacement.Address,
                    replacement.Port,
                    replacement.CertificateFingerprint);
            }

            throw new InvalidOperationException(replacementProbe.Message);
        }

        throw new InvalidOperationException(initial.Message);
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        _thumbnailCancellation?.Cancel();
        _client?.Dispose();
        _client = null;
        _rootItem = null;
        _current = null;
        _history.Clear();
        _items.Clear();
        _folderRoots.Clear();
        ConnectionStatusText.Text = "Not connected";
        DeviceDetailsText.Text = string.Empty;
        PathText.Text = "Connect to a phone to browse files";
        OperationStatusText.Text = "Ready";
        UpdateActionState();
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_history.Count > 0)
        {
            await NavigateAsync(_history.Pop(), addToHistory: false);
        }
    }

    private async void UpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not { Path.Count: > 1 })
        {
            return;
        }

        var parentPath = _current.Path.Take(_current.Path.Count - 1).ToArray();
        var parent = parentPath[^1];
        await NavigateAsync(
            new NavigationEntry(parent.Id, parent.Name, parentPath),
            addToHistory: true);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not null)
        {
            await NavigateAsync(_current, addToHistory: false);
        }
    }

    private async void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not FolderNode { IsPlaceholder: false } node
            || node.Id == _current?.Id)
        {
            return;
        }

        var path = node.Path().Select(item => new PathSegment(item.Id, item.Name)).ToArray();
        await NavigateAsync(new NavigationEntry(node.Id, node.Name, path), addToHistory: true);
    }

    private async void FolderTreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { DataContext: FolderNode node })
        {
            await LoadFolderNodeAsync(node);
        }
    }

    private async Task LoadFolderNodeAsync(FolderNode node)
    {
        if (_client is null || node.IsLoaded || node.IsPlaceholder)
        {
            return;
        }

        try
        {
            var children = await _client.GetChildrenAsync(node.Id);
            SynchronizeFolderNode(node, children);
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private async void Items_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var selected = sender switch
        {
            DataGrid grid => grid.SelectedItem as RemoteItem,
            ListBox list => list.SelectedItem as RemoteItem,
            _ => null
        };
        if (selected is null)
        {
            return;
        }
        await OpenItemAsync(selected);
    }

    private async Task OpenItemAsync(RemoteItem selected)
    {
        if (!selected.IsDirectory)
        {
            await OpenRemoteFileAsync(selected);
            return;
        }
        if (_current is null)
        {
            return;
        }

        var path = _current.Path.Append(new PathSegment(selected.Id, selected.Name)).ToArray();
        await NavigateAsync(
            new NavigationEntry(selected.Id, selected.Name, path),
            addToHistory: true);
    }

    private void Items_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateActionState();

    private void ItemCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: RemoteItem item } checkBox)
        {
            item.IsChecked = checkBox.IsChecked == true;
        }
        UpdateActionState();
    }

    private void Items_PreviewMouseMove(object sender, MouseEventArgs e)
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
                _client!.ConnectionKey,
                _client.DeviceName,
                selected.ToArray()));
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private void OpenFolderWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null || _current is null)
        {
            return;
        }

        var selectedFolder = SelectedItems().FirstOrDefault(item => item.IsDirectory);
        var path = _current.Path.Select(item => (item.Id, item.Name));
        if (selectedFolder is not null)
        {
            path = path.Append((selectedFolder.Id, selectedFolder.Name));
        }
        new FolderWindow(_client, path.ToArray()) { Owner = this }.Show();
    }

    private void CopySelectionButton_Click(object sender, RoutedEventArgs e) =>
        SetRemoteClipboard(cut: false);

    private void CutSelectionButton_Click(object sender, RoutedEventArgs e) =>
        SetRemoteClipboard(cut: true);

    private void SetRemoteClipboard(bool cut)
    {
        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            ShowError("Check or select one or more files or folders first.");
            return;
        }
        RemoteClipboard.Set(_client!, selected, cut);
        OperationStatusText.Text = $"{(cut ? "Cut" : "Copied")} {selected.Count} item(s).";
        UpdateActionState();
    }

    private async void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected() || _current is null)
        {
            return;
        }
        if (!RemoteClipboard.HasItems)
        {
            ShowError("The Phone Transfer clipboard is empty.");
            return;
        }
        await RunOperationAsync("Pasting items...", async () =>
        {
            await RemoteClipboard.PasteAsync(_client!, _current.Id);
            await NavigateAsync(_current, addToHistory: false);
        });
    }

    private async void OpenMediaButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem() is not { } item)
        {
            ShowError("Select a file first.");
            return;
        }
        await OpenRemoteFileAsync(item);
    }

    private async Task OpenRemoteFileAsync(RemoteItem item)
    {
        if (_client is null)
        {
            ShowError("Connect to a phone first.");
            return;
        }
        if (item.IsDirectory)
        {
            return;
        }

        try
        {
            RemoteFileLauncher.Open(
                this,
                _client,
                item,
                _items.Where(candidate => candidate.IsMedia).ToArray(),
                status => OperationStatusText.Text = status,
                ShowTransfersWindow);
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        await Task.CompletedTask;
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected() || _current is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Choose files to upload",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await UploadPathsAsync(dialog.FileNames, _current.Id, _current.Name);
    }

    private async void UploadFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected() || _current is null)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder to upload",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await UploadPathsAsync([dialog.FolderName], _current.Id, _current.Name);
    }

    private async Task UploadPathsAsync(
        IReadOnlyList<string> paths,
        string destinationId,
        string destinationName)
    {
        var validPaths = paths.Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();
        if (validPaths.Length == 0)
        {
            return;
        }

        foreach (var path in validPaths)
        {
            var itemName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            var totalBytes = PathSize(path);
            TransferManager.Instance.Enqueue(
                _client!,
                itemName,
                "Upload",
                totalBytes,
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
                completed: () => _ = RefreshFolderIfCurrentAsync(destinationId),
                location: destinationName);
        }

        ShowTransfersWindow();
        OperationStatusText.Text =
            $"Queued {validPaths.Length} item(s) for upload to {destinationName}.";
        await Task.CompletedTask;
    }

    private void FilesHost_DragOver(object sender, DragEventArgs e)
    {
        var accepted = _client is not null
            && _current is not null
            && (e.Data.GetDataPresent(DataFormats.FileDrop)
                || e.Data.GetDataPresent(RemoteItemsFormat));
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        DropHint.Visibility = accepted ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private async void FilesHost_Drop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        if (_current is not null
            && e.Data.GetDataPresent(RemoteItemsFormat)
            && e.Data.GetData(RemoteItemsFormat) is RemoteDragPayload payload)
        {
            try
            {
                RemoteClipboard.Set(
                    payload.ConnectionKey,
                    payload.DeviceName,
                    payload.Items,
                    cut: false);
                await RemoteClipboard.PasteAsync(_client!, _current.Id);
                await NavigateAsync(_current, addToHistory: false);
            }
            catch (Exception exception)
            {
                ShowError(exception.Message);
            }
            return;
        }
        if (_current is null
            || !e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        var target = FindDataContext<RemoteItem>(e.OriginalSource as DependencyObject);
        var destinationId = target is { IsDirectory: true } ? target.Id : _current.Id;
        var destinationName = target is { IsDirectory: true } ? target.Name : _current.Name;
        await UploadPathsAsync(paths, destinationId, destinationName);
    }

    private void FilesHost_DragLeave(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected())
        {
            return;
        }

        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            ShowError("Select one or more files or folders to download.");
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Choose a download folder",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var item in selected)
        {
            TransferManager.Instance.Enqueue(
                _client!,
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

        ShowTransfersWindow();
        OperationStatusText.Text =
            $"Queued {selected.Count} item(s) for download to {dialog.FolderName}.";
        await Task.CompletedTask;
    }

    private async void NewFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected() || _current is null)
        {
            return;
        }

        var name = PromptWindow.Show(this, "New folder", "Folder name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await RunOperationAsync("Creating folder...", async () =>
        {
            await _client!.CreateFolderAsync(_current.Id, name.Trim());
            await NavigateAsync(_current, addToHistory: false);
        });
    }

    private async void MoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected() || _rootItem is null || _current is null)
        {
            return;
        }

        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            ShowError("Select one or more files or folders to move.");
            return;
        }

        var destination = MoveWindow.Choose(
            this,
            _client!,
            _rootItem,
            selected.Where(item => item.IsDirectory).Select(item => item.Id),
            "Move");
        if (destination is null)
        {
            return;
        }

        await RunOperationAsync($"Moving {selected.Count} item(s)...", async () =>
        {
            foreach (var item in selected)
            {
                await _client!.MoveAsync(item.Id, destination.Id);
            }
            await NavigateAsync(_current, addToHistory: false);
            OperationStatusText.Text = $"Moved {selected.Count} item(s) to {destination.Name}.";
        });
    }

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected() || _rootItem is null || _current is null)
        {
            return;
        }

        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            ShowError("Select one or more files or folders to copy.");
            return;
        }

        var destination = MoveWindow.Choose(
            this,
            _client!,
            _rootItem,
            selected.Where(item => item.IsDirectory).Select(item => item.Id),
            "Copy");
        if (destination is null)
        {
            return;
        }

        await RunOperationAsync($"Copying {selected.Count} item(s)...", async () =>
        {
            foreach (var item in selected)
            {
                await _client!.CopyAsync(item.Id, destination.Id);
            }
            await NavigateAsync(_current, addToHistory: false);
            OperationStatusText.Text = $"Copied {selected.Count} item(s) to {destination.Name}.";
        });
    }

    private async void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected() || SelectedItem() is not RemoteItem item)
        {
            return;
        }

        var name = PromptWindow.Show(this, "Rename", "New name", item.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == item.Name)
        {
            return;
        }

        await RunOperationAsync("Renaming...", async () =>
        {
            await _client!.RenameAsync(item.Id, name.Trim());
            await NavigateAsync(_current!, addToHistory: false);
        });
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected())
        {
            return;
        }

        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            return;
        }

        var message = selected.Count == 1
            ? $"Permanently delete \"{selected[0].Name}\" from the phone?"
            : $"Permanently delete these {selected.Count} items from the phone?";
        if (MessageBox.Show(
                this,
                message,
                "Confirm delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunOperationAsync("Deleting...", async () =>
        {
            foreach (var item in selected)
            {
                await _client!.DeleteAsync(item.Id);
            }
            await NavigateAsync(_current!, addToHistory: false);
        });
    }

    private async Task NavigateAsync(NavigationEntry destination, bool addToHistory)
    {
        if (_client is null)
        {
            return;
        }

        await RunOperationAsync($"Opening {destination.Name}...", async () =>
        {
            var children = await _client.GetChildrenAsync(destination.Id);
            if (addToHistory && _current is not null && _current.Id != destination.Id)
            {
                _history.Push(_current);
            }

            _current = destination;
            _items.Clear();
            foreach (var item in children
                         .OrderByDescending(item => item.IsDirectory)
                         .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
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

            var folderNode = FindFolderNode(destination.Id);
            if (folderNode is not null)
            {
                SynchronizeFolderNode(folderNode, children);
            }

            PathText.Text = string.Join(" > ", destination.Path.Select(segment => segment.Name));
            var totalSize = children.Where(item => !item.IsDirectory).Sum(item => Math.Max(0, item.Size));
            OperationStatusText.Text = totalSize > 0
                ? $"{children.Count} item(s) | {FormatSize(totalSize)}"
                : $"{children.Count} item(s)";
            UpdateActionState();
            await LoadThumbnailsIfNeededAsync();
        });
    }

    private void SynchronizeFolderNode(FolderNode node, IReadOnlyList<RemoteItem> children)
    {
        var existing = node.Children
            .Where(child => !child.IsPlaceholder)
            .ToDictionary(child => child.Id, StringComparer.Ordinal);
        node.Children.Clear();
        foreach (var folder in children
                     .Where(item => item.IsDirectory)
                     .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (existing.TryGetValue(folder.Id, out var current))
            {
                current.Name = folder.Name;
                node.Children.Add(current);
            }
            else
            {
                node.Children.Add(FolderNode.Create(folder.Id, folder.Name, node));
            }
        }
        node.IsLoaded = true;
    }

    private FolderNode? FindFolderNode(string id)
    {
        foreach (var root in _folderRoots)
        {
            var match = FindFolderNode(root, id);
            if (match is not null)
            {
                return match;
            }
        }
        return null;
    }

    private static FolderNode? FindFolderNode(FolderNode node, string id)
    {
        if (node.Id == id)
        {
            return node;
        }
        foreach (var child in node.Children.Where(child => !child.IsPlaceholder))
        {
            var match = FindFolderNode(child, id);
            if (match is not null)
            {
                return match;
            }
        }
        return null;
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
        await SetViewModeAsync("Details");

    private async void ViewList_Click(object sender, RoutedEventArgs e) =>
        await SetViewModeAsync("List");

    private async void ViewThumbnails_Click(object sender, RoutedEventArgs e) =>
        await SetViewModeAsync("Thumbnails");

    private async Task SetViewModeAsync(string mode)
    {
        FilesGrid.Visibility = mode == "Details" ? Visibility.Visible : Visibility.Collapsed;
        FilesList.Visibility = mode == "List" ? Visibility.Visible : Visibility.Collapsed;
        ThumbnailList.Visibility = mode == "Thumbnails" ? Visibility.Visible : Visibility.Collapsed;
        ViewModeButton.Content = $"View: {mode}";
        await LoadThumbnailsIfNeededAsync();
        UpdateActionState();
    }

    private async Task LoadThumbnailsIfNeededAsync()
    {
        _thumbnailCancellation?.Cancel();
        if (_client is null || ThumbnailList.Visibility != Visibility.Visible)
        {
            return;
        }

        _thumbnailCancellation = new CancellationTokenSource();
        var cancellationToken = _thumbnailCancellation.Token;
        var client = _client;
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
                    var bytes = await client.GetThumbnailAsync(item.Id, 256, cancellationToken);
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
                    // A missing provider thumbnail should not interrupt folder browsing.
                }
                finally
                {
                    throttle.Release();
                }
            })
            .ToArray();
        await Task.WhenAll(tasks);
    }

    private void ConnectionPaneToggle_Click(object sender, RoutedEventArgs e)
    {
        var collapse = ConnectionColumn.Width.Value > 0;
        ConnectionColumn.Width = collapse ? new GridLength(0) : new GridLength(330);
        ConnectionPane.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        ConnectionPaneToggle.Content = collapse ? ">" : "<";
        ConnectionPaneToggle.ToolTip = collapse
            ? "Expand connection panel"
            : "Collapse connection panel";
    }

    private void FolderPaneButton_Click(object sender, RoutedEventArgs e)
    {
        var collapse = FolderPane.Visibility == Visibility.Visible;
        if (collapse)
        {
            if (FolderColumn.Width.Value > 0)
            {
                _folderPaneWidth = FolderColumn.Width;
            }
            FolderColumn.MinWidth = 0;
            FolderColumn.Width = new GridLength(0);
            FolderSplitterColumn.Width = new GridLength(0);
            FolderPane.Visibility = Visibility.Collapsed;
            FolderSplitter.Visibility = Visibility.Collapsed;
            FolderPaneButton.Content = "Folders: Off";
            return;
        }

        FolderColumn.MinWidth = 120;
        FolderColumn.Width = _folderPaneWidth.Value >= 120
            ? _folderPaneWidth
            : new GridLength(220);
        FolderSplitterColumn.Width = new GridLength(6);
        FolderPane.Visibility = Visibility.Visible;
        FolderSplitter.Visibility = Visibility.Visible;
        FolderPaneButton.Content = "Folders: On";
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
            return FilesGrid.SelectedItems.Cast<RemoteItem>().ToList();
        }
        if (FilesList.Visibility == Visibility.Visible)
        {
            return FilesList.SelectedItems.Cast<RemoteItem>().ToList();
        }
        return ThumbnailList.SelectedItems.Cast<RemoteItem>().ToList();
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

    private async Task RunOperationAsync(string status, Func<Task> operation)
    {
        try
        {
            SetBusy(true, status);
            await operation();
        }
        catch (Exception exception)
        {
            OperationStatusText.Text = "Operation failed";
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool EnsureConnected()
    {
        if (_client is not null)
        {
            return true;
        }

        ShowError("Connect to a phone first.");
        return false;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busyDepth = busy ? _busyDepth + 1 : Math.Max(0, _busyDepth - 1);
        Mouse.OverrideCursor = _busyDepth > 0 ? Cursors.Wait : null;
        if (status is not null)
        {
            OperationStatusText.Text = status;
        }
        UpdateActionState();
    }

    private void UpdateActionState()
    {
        if (!IsInitialized)
        {
            return;
        }

        var busy = _busyDepth > 0;
        var connected = _client is not null;
        var browsing = connected && _current is not null;

        DiscoverButton.IsEnabled = !busy;
        HotspotButton.IsEnabled = !busy;
        OpenHotspotSettingsButton.IsEnabled = !busy;
        ConnectDiscoveredButton.IsEnabled =
            !busy && DiscoveredDevicesCombo.SelectedItem is DiscoveredDevice;
        TrustedDevicesCombo.IsEnabled = !busy;
        SwitchDeviceButton.IsEnabled = !busy && _trustedDevices.Count > 0;
        ConnectButton.IsEnabled = !busy;
        HostTextBox.IsEnabled = !busy;
        PortTextBox.IsEnabled = !busy;
        TokenTextBox.IsEnabled = !busy;
        DiscoveredDevicesCombo.IsEnabled = !busy;
        RememberDeviceCheckBox.IsEnabled = !busy;
        ForgetDeviceButton.IsEnabled = !busy && ConnectionProfileStore.LoadAll().Count > 0;

        DisconnectButton.IsEnabled = connected && !busy;
        DisconnectButton.Content = connected ? "Disconnect" : "Not connected";
        DisconnectButton.Style = (Style)FindResource(
            connected ? "ConnectedDisconnectButton" : "DisconnectedButton");
        RefreshButton.IsEnabled = browsing && !busy;
        BackButton.IsEnabled = browsing && _history.Count > 0 && !busy;
        UpButton.IsEnabled = browsing && _current!.Path.Count > 1 && !busy;
        FileActionsPanel.IsEnabled = browsing && !busy;
        FilesGrid.IsEnabled = browsing && !busy;
        FilesList.IsEnabled = browsing && !busy;
        ThumbnailList.IsEnabled = browsing && !busy;
        FolderTree.IsEnabled = connected && !busy;
        ViewModeButton.IsEnabled = browsing && !busy;
        OpenMediaButton.IsEnabled = browsing
            && !busy
            && SelectedItem() is { IsDirectory: false };
        OpenFolderWindowButton.IsEnabled = browsing && !busy;
        CopySelectionButton.IsEnabled = browsing && !busy && SelectedItems().Count > 0;
        CutSelectionButton.IsEnabled = browsing && !busy && SelectedItems().Count > 0;
        PasteButton.IsEnabled = browsing && !busy && RemoteClipboard.HasItems;
        SettingsButton.IsEnabled = !busy;
        FilesHost.AllowDrop = browsing;
    }

    private async Task RefreshFolderIfCurrentAsync(string folderId)
    {
        if (_current?.Id == folderId)
        {
            await NavigateAsync(_current, addToHistory: false);
        }
    }

    private static T? FindDataContext<T>(DependencyObject? source) where T : class
    {
        for (var current = source; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: T value })
            {
                return value;
            }
        }
        return null;
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

    private static long PathSize(string path)
    {
        if (File.Exists(path))
        {
            return new FileInfo(path).Length;
        }
        if (!Directory.Exists(path))
        {
            return 0;
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

    private static string NormalizeFingerprint(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
    }

    private void ShowError(string message)
    {
        MessageBox.Show(this, message, "Phone Transfer", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override void OnClosed(EventArgs e)
    {
        TransferManager.Instance.Changed -= TransferManager_Changed;
        _thumbnailCancellation?.Cancel();
        _client?.Dispose();
        base.OnClosed(e);
    }

    private sealed record PathSegment(string Id, string Name);

    private sealed record ConnectedEndpoint(
        string Host,
        int Port,
        string? CertificateFingerprint);

    private sealed record NavigationEntry(
        string Id,
        string Name,
        IReadOnlyList<PathSegment> Path);
}
