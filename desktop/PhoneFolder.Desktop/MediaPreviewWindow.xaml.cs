using PhoneFolder.Desktop.Models;
using PhoneFolder.Desktop.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PhoneFolder.Desktop;

public partial class MediaPreviewWindow : Window
{
    private readonly RemoteClient _client;
    private readonly IReadOnlyList<RemoteItem> _items;
    private readonly DispatcherTimer _positionTimer;
    private readonly bool _autoOpenDefaultApplication;
    private RemoteMediaServer? _server;
    private int _index;
    private int _rotation;
    private double _zoom = 1;
    private bool _isPlaying;
    private bool _isSeeking;
    private bool _isFullScreen;
    private string? _playlistPath;
    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;
    private ResizeMode _previousResizeMode;

    public MediaPreviewWindow(
        RemoteClient client,
        RemoteItem item,
        IReadOnlyList<RemoteItem>? folderMedia = null,
        bool autoOpenDefaultApplication = false)
    {
        InitializeComponent();
        _client = client;
        _autoOpenDefaultApplication = autoOpenDefaultApplication;
        _items = item.IsImage
            ? (folderMedia ?? [item]).Where(candidate => candidate.IsImage).ToArray()
            : [item];
        _index = Math.Max(0, _items.ToList().FindIndex(candidate => candidate.Id == item.Id));
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _positionTimer.Tick += PositionTimer_Tick;
        Loaded += async (_, _) => await LoadCurrentAsync(openDefaultForPlayback: true);
    }

    private RemoteItem CurrentItem => _items[_index];

    private async Task LoadCurrentAsync(bool openDefaultForPlayback)
    {
        try
        {
            Player.Stop();
            Player.Source = null;
            _isPlaying = false;
            _positionTimer.Stop();
            await DisposeServerAsync();

            var item = CurrentItem;
            Title = $"{item.Name} - Phone Transfer";
            Photo.Source = null;
            Photo.Visibility = Visibility.Collapsed;
            Player.Visibility = Visibility.Collapsed;
            AudioPlaceholder.Visibility = Visibility.Collapsed;
            CenterPlayButton.Visibility = Visibility.Collapsed;
            PlaybackControls.Visibility = Visibility.Collapsed;
            SeekPanel.Visibility = Visibility.Collapsed;
            ImageControls.Visibility = item.IsImage ? Visibility.Visible : Visibility.Collapsed;
            DefaultAppButton.Visibility = item.IsImage ? Visibility.Collapsed : Visibility.Visible;
            PreviousButton.IsEnabled = _index > 0;
            NextButton.IsEnabled = _index < _items.Count - 1;
            SetRotation(0);
            SetZoom(1);

            if (item.IsImage)
            {
                StatusText.Text = "Loading image directly from the phone...";
                Photo.Source = await LoadOrientedImageAsync(item);
                Photo.Visibility = Visibility.Visible;
                StatusText.Text = "Image loaded directly from the phone";
                return;
            }

            Player.Visibility = Visibility.Visible;
            CenterPlayButton.Visibility = Visibility.Visible;
            PlaybackControls.Visibility = Visibility.Visible;
            SeekPanel.Visibility = Visibility.Visible;
            if (item.IsAudio)
            {
                AudioPlaceholder.Visibility = Visibility.Visible;
            }

            EnsureServer();
            if (openDefaultForPlayback
                && _autoOpenDefaultApplication
                && OpenInDefaultApplication())
            {
                StatusText.Text =
                    "Opened in the default Windows app. Keep this window open while it plays.";
            }
            else
            {
                StartInternalPlayback();
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = "Unable to open this item";
            MessageBox.Show(this, exception.Message, "Phone Transfer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<BitmapSource> LoadOrientedImageAsync(RemoteItem item)
    {
        using var response = await _client.OpenContentStreamAsync(item.Id, null);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync();
        using var memory = new MemoryStream();
        await input.CopyToAsync(memory);
        memory.Position = 0;

        var decoder = BitmapDecoder.Create(
            memory,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        BitmapSource source = decoder.Frames[0];
        var orientation = ReadExifOrientation(decoder.Frames[0].Metadata as BitmapMetadata);
        var angle = orientation switch
        {
            3 => 180,
            6 => 90,
            8 => 270,
            _ => 0
        };
        if (angle != 0)
        {
            source = new TransformedBitmap(source, new RotateTransform(angle));
        }
        source.Freeze();
        return source;
    }

    private static int ReadExifOrientation(BitmapMetadata? metadata)
    {
        if (metadata is null)
        {
            return 1;
        }
        try
        {
            return metadata.GetQuery("/app1/ifd/{ushort=274}") switch
            {
                ushort value => value,
                uint value => (int)value,
                _ => 1
            };
        }
        catch
        {
            return 1;
        }
    }

    private void EnsureServer()
    {
        _server ??= new RemoteMediaServer(_client, CurrentItem);
    }

    private void EnsureInternalSource()
    {
        EnsureServer();
        Player.Source ??= _server!.StreamUri;
    }

    private void StartInternalPlayback()
    {
        EnsureInternalSource();
        Player.Play();
        _isPlaying = true;
        CenterPlayButton.Visibility = Visibility.Collapsed;
        StatusText.Text = "Playing in Phone Transfer";
    }

    private bool OpenInDefaultApplication()
    {
        try
        {
            EnsureServer();
            DeletePlaylist();
            _playlistPath = Path.Combine(
                Path.GetTempPath(),
                $"PhoneTransfer-{Guid.NewGuid():N}.m3u8");
            File.WriteAllText(
                _playlistPath,
                $"#EXTM3U{Environment.NewLine}{_server!.StreamUri}{Environment.NewLine}");
            Process.Start(new ProcessStartInfo(_playlistPath)
            {
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OpenDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = OpenInDefaultApplication()
            ? "Opened in the default Windows app. Keep this window open while it plays."
            : "Windows could not open this file type in a default app.";
    }

    private void CenterPlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            Player.Pause();
            _isPlaying = false;
            StatusText.Text = "Paused";
            return;
        }
        StartInternalPlayback();
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e) => StartInternalPlayback();

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        Player.Pause();
        _isPlaying = false;
        CenterPlayButton.Visibility = Visibility.Collapsed;
        StatusText.Text = "Paused";
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        Player.Stop();
        _isPlaying = false;
        CenterPlayButton.Visibility = Visibility.Collapsed;
        SeekSlider.Value = 0;
        StatusText.Text = "Stopped";
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (Player.NaturalDuration.HasTimeSpan)
        {
            SeekSlider.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
            DurationText.Text = FormatTime(Player.NaturalDuration.TimeSpan);
            _positionTimer.Start();
        }
        StatusText.Text = "Playing in Phone Transfer";
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        _isPlaying = false;
        _positionTimer.Stop();
        StatusText.Text = "Playback finished";
    }

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (_isSeeking || !Player.NaturalDuration.HasTimeSpan)
        {
            return;
        }
        SeekSlider.Value = Player.Position.TotalSeconds;
        CurrentTimeText.Text = FormatTime(Player.Position);
    }

    private void SeekSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _isSeeking = true;

    private void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EnsureInternalSource();
        Player.Position = TimeSpan.FromSeconds(SeekSlider.Value);
        CurrentTimeText.Text = FormatTime(Player.Position);
        _isSeeking = false;
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_index > 0)
        {
            _index--;
            await LoadCurrentAsync(openDefaultForPlayback: false);
        }
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_index < _items.Count - 1)
        {
            _index++;
            await LoadCurrentAsync(openDefaultForPlayback: false);
        }
    }

    private void RotateButton_Click(object sender, RoutedEventArgs e) =>
        SetRotation((_rotation + 90) % 360);

    private void SetRotation(int degrees)
    {
        _rotation = degrees;
        RotatableSurface.LayoutTransform = new RotateTransform(_rotation);
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e) =>
        SetZoom(_zoom + 0.25);

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) =>
        SetZoom(_zoom - 0.25);

    private void ResetZoomButton_Click(object sender, RoutedEventArgs e) => SetZoom(1);

    private void Photo_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        SetZoom(_zoom + (e.Delta > 0 ? 0.25 : -0.25));
        e.Handled = true;
    }

    private void SetZoom(double value)
    {
        _zoom = Math.Clamp(value, 0.25, 4);
        Photo.RenderTransform = new ScaleTransform(_zoom, _zoom);
        ZoomText.Text = $"{_zoom * 100:0}%";
    }

    private void FullScreenButton_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private void MediaSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
    }

    private void ToggleFullScreen()
    {
        if (!_isFullScreen)
        {
            _previousWindowStyle = WindowStyle;
            _previousWindowState = WindowState;
            _previousResizeMode = ResizeMode;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            FullScreenButton.Content = "Exit full screen";
            _isFullScreen = true;
            return;
        }

        WindowStyle = _previousWindowStyle;
        ResizeMode = _previousResizeMode;
        WindowState = _previousWindowState;
        FullScreenButton.Content = "Full screen";
        _isFullScreen = false;
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_isFullScreen)
            {
                ToggleFullScreen();
            }
            else
            {
                Close();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Space && !CurrentItem.IsImage)
        {
            CenterPlayButton_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.R)
        {
            RotateButton_Click(sender, e);
            e.Handled = true;
        }
        else if (CurrentItem.IsImage && (e.Key == Key.Add || e.Key == Key.OemPlus))
        {
            SetZoom(_zoom + 0.25);
            e.Handled = true;
        }
        else if (CurrentItem.IsImage && (e.Key == Key.Subtract || e.Key == Key.OemMinus))
        {
            SetZoom(_zoom - 0.25);
            e.Handled = true;
        }
        else if (CurrentItem.IsImage && e.Key == Key.D0)
        {
            SetZoom(1);
            e.Handled = true;
        }
        else if (e.Key == Key.F)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
        else if (CurrentItem.IsImage && e.Key == Key.Left && _index > 0)
        {
            _index--;
            await LoadCurrentAsync(openDefaultForPlayback: false);
            e.Handled = true;
        }
        else if (CurrentItem.IsImage && e.Key == Key.Right && _index < _items.Count - 1)
        {
            _index++;
            await LoadCurrentAsync(openDefaultForPlayback: false);
            e.Handled = true;
        }
    }

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

    private async Task DisposeServerAsync()
    {
        if (_server is null)
        {
            return;
        }
        await _server.DisposeAsync();
        _server = null;
    }

    private void DeletePlaylist()
    {
        if (string.IsNullOrWhiteSpace(_playlistPath))
        {
            return;
        }
        try
        {
            File.Delete(_playlistPath);
        }
        catch
        {
        }
        _playlistPath = null;
    }

    protected override async void OnClosed(EventArgs e)
    {
        _positionTimer.Stop();
        Player.Stop();
        await DisposeServerAsync();
        DeletePlaylist();
        base.OnClosed(e);
    }
}
