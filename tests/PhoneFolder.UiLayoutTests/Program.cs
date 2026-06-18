using PhoneFolder.Desktop;
using PhoneFolder.Desktop.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var application = new App
            {
            };
            application.InitializeComponent();
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var renderDirectory = args.Length == 2 && args[0] == "--render"
                ? Path.GetFullPath(args[1])
                : null;
            ValidateMainWindow(renderDirectory);
            ValidateFolderWindow(renderDirectory);
            ValidateCompactTables();
            ValidateProgressBar();
            ValidateMainCommandFlows();
            ValidateCommandBridge();
            WindowLifecycleTests.Run(application);

            Console.WriteLine("PASS: WPF dark-theme layout, command-state, and window-lifecycle checks.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: {exception}");
            return 1;
        }
    }

    private static void ValidateMainWindow(string? renderDirectory)
    {
        using var scope = new WindowScope(new MainWindow());
        double? narrowActionWidth = null;
        foreach (var width in new[] { 1040d, 1180d, 1380d, 1600d })
        {
            Layout(scope.Window, width, 680);
            AssertToolbarLayout(
                Require<UniformGrid>(scope.Window, "MainHeaderButtonsPanel"),
                $"main header at {width:0}px");
            var actionWidth = AssertToolbarLayout(
                Require<UniformGrid>(scope.Window, "FileActionsPanel"),
                $"main file actions at {width:0}px");
            if (narrowActionWidth is null)
            {
                narrowActionWidth = actionWidth;
            }
            else if (Math.Abs(width - 1600) < 0.1)
            {
                Assert(
                    actionWidth > narrowActionWidth + 10,
                    "Main file-action buttons do not grow and shrink with the window.");
            }
        }

        AssertDarkWindowChrome(scope.Window, "Main window");
        Assert(
            FindVisualChild<Menu>(scope.Window) is null,
            "The main window still contains the removed File/Edit/View menu bar.");
        Require<ListBox>(scope.Window, "FolderTabsList");
        AssertDisabled(scope.Window, "DownloadButton");
        AssertDisabled(scope.Window, "CopySelectionButton");
        AssertDisabled(scope.Window, "CutSelectionButton");
        AssertDisabled(scope.Window, "PasteButton");
        AssertDisabled(scope.Window, "CopyToButton");
        AssertDisabled(scope.Window, "MoveToButton");
        AssertDisabled(scope.Window, "RenameButton");
        AssertDisabled(scope.Window, "DeleteButton");
        Require<Button>(scope.Window, "ViewModeButton");
        Require<Button>(scope.Window, "SortModeButton");
        if (renderDirectory is not null)
        {
            Layout(scope.Window, 1040, 680);
            RenderWindowContent(
                scope.Window,
                Path.Combine(renderDirectory, "main-dark-1040x680.png"));
            Layout(scope.Window, 1380, 860);
            RenderWindowContent(
                scope.Window,
                Path.Combine(renderDirectory, "main-dark-1380x860.png"));
        }
    }

    private static void ValidateFolderWindow(string? renderDirectory)
    {
        RemoteClipboard.Clear();
        using var client = new RemoteClient("127.0.0.1", 8765, "layout-test");
        using var scope = new WindowScope(
            new FolderWindow(client, [("root", "Phone")]));
        double? narrowActionWidth = null;
        foreach (var width in new[] { 720d, 800d, 980d, 1200d })
        {
            Layout(scope.Window, width, 440);
            AssertToolbarLayout(
                Require<UniformGrid>(scope.Window, "FolderNavigationPanel"),
                $"folder navigation at {width:0}px");
            var actionWidth = AssertToolbarLayout(
                Require<UniformGrid>(scope.Window, "FolderActionsPanel"),
                $"folder actions at {width:0}px");
            if (narrowActionWidth is null)
            {
                narrowActionWidth = actionWidth;
            }
            else if (Math.Abs(width - 1200) < 0.1)
            {
                Assert(
                    actionWidth > narrowActionWidth + 10,
                    "Folder action buttons do not grow and shrink with the window.");
            }
        }

        AssertDarkWindowChrome(scope.Window, "Folder window");
        AssertDisabled(scope.Window, "DownloadButton");
        AssertDisabled(scope.Window, "CopyButton");
        AssertDisabled(scope.Window, "CutButton");
        AssertDisabled(scope.Window, "PasteButton");
        AssertDisabled(scope.Window, "CopyToButton");
        AssertDisabled(scope.Window, "MoveToButton");
        AssertDisabled(scope.Window, "RenameButton");
        AssertDisabled(scope.Window, "DeleteButton");

        var details = Require<DataGrid>(scope.Window, "FilesGrid");
        var compactStyle = details.RowStyle
            ?? throw new InvalidOperationException("Folder details view has no compact row style.");
        var heightSetter = compactStyle.Setters
            .OfType<Setter>()
            .FirstOrDefault(setter => setter.Property == FrameworkElement.HeightProperty);
        Assert(
            heightSetter?.Value is double height && Math.Abs(height - 26) < 0.1,
            "Folder detail rows are not fixed at the compact 26-pixel height.");
        Require<ListBox>(scope.Window, "FilesList");
        Require<ListBox>(scope.Window, "ThumbnailList");
        Require<Button>(scope.Window, "ViewModeButton");
        Require<Button>(scope.Window, "SortModeButton");

        var first = new PhoneFolder.Desktop.Models.RemoteItem
        {
            Id = "first",
            Name = "first.txt",
            MimeType = "text/plain"
        };
        var second = new PhoneFolder.Desktop.Models.RemoteItem
        {
            Id = "second",
            Name = "second.txt",
            MimeType = "text/plain"
        };
        var items = details.ItemsSource is ListCollectionView view
            ? view.SourceCollection as System.Collections.IList
            : details.ItemsSource as System.Collections.IList;
        if (items is null)
        {
            throw new InvalidOperationException("Folder details items are not mutable.");
        }
        items.Add(first);
        items.Add(second);
        details.SelectedItem = first;
        details.UpdateLayout();

        AssertEnabled(scope.Window, "DownloadButton");
        AssertEnabled(scope.Window, "CopyButton");
        AssertEnabled(scope.Window, "CutButton");
        AssertEnabled(scope.Window, "CopyToButton");
        AssertEnabled(scope.Window, "MoveToButton");
        AssertEnabled(scope.Window, "RenameButton");
        AssertEnabled(scope.Window, "DeleteButton");

        RemoteClipboard.Set(client, [first], cut: false);
        AssertEnabled(scope.Window, "PasteButton");
        details.SelectedItems.Add(second);
        details.UpdateLayout();
        AssertDisabled(scope.Window, "RenameButton");
        AssertEnabled(scope.Window, "DeleteButton");
        RemoteClipboard.Clear();
        AssertDisabled(scope.Window, "PasteButton");
        if (renderDirectory is not null)
        {
            Layout(scope.Window, 720, 440);
            RenderWindowContent(
                scope.Window,
                Path.Combine(renderDirectory, "folder-dark-720x440.png"));
            Layout(scope.Window, 980, 640);
            RenderWindowContent(
                scope.Window,
                Path.Combine(renderDirectory, "folder-dark-980x640.png"));
        }
    }

    private static void ValidateCompactTables()
    {
        using var trustedScope = new WindowScope(new TrustedDevicesWindow());
        AssertCompactDataGrid(
            Require<DataGrid>(trustedScope.Window, "ProfilesGrid"),
            "Trusted phone list");

        using var transferScope = new WindowScope(new TransferWindow());
        AssertCompactDataGrid(
            Require<DataGrid>(transferScope.Window, "JobsGrid"),
            "Transfer list");
    }

    private static void AssertCompactDataGrid(DataGrid grid, string label)
    {
        var compactStyle = grid.RowStyle
            ?? throw new InvalidOperationException($"{label} has no compact row style.");
        var heightSetter = compactStyle.Setters
            .OfType<Setter>()
            .FirstOrDefault(setter => setter.Property == FrameworkElement.HeightProperty);
        Assert(
            heightSetter?.Value is double height && Math.Abs(height - 26) < 0.1,
            $"{label} rows are not fixed at the compact 26-pixel height.");
    }

    private static void ValidateProgressBar()
    {
        var progress = new ProgressBar
        {
            Width = 170,
            Height = 22,
            Minimum = 0,
            Maximum = 100,
            Value = 42,
            Tag = "42%",
            Style = (Style)Application.Current.FindResource(typeof(ProgressBar))
        };
        progress.Measure(new Size(170, 22));
        progress.Arrange(new Rect(0, 0, 170, 22));
        progress.ApplyTemplate();
        progress.UpdateLayout();

        var indicator = progress.Template.FindName("PART_Indicator", progress) as FrameworkElement
            ?? throw new InvalidOperationException("Progress indicator template part is missing.");
        var label = FindVisualChild<TextBlock>(progress)
            ?? throw new InvalidOperationException("Progress percentage label is missing.");

        Assert(
            indicator.ActualWidth > 65 && indicator.ActualWidth < 78,
            $"Progress fill width is incorrect at 42%: {indicator.ActualWidth:0.##}.");
        var labelOrigin = label.TranslatePoint(new Point(0, 0), progress);
        Assert(labelOrigin.X >= 0 && labelOrigin.Y >= 0, "Progress percentage starts outside the bar.");
        Assert(
            labelOrigin.X + label.ActualWidth <= progress.ActualWidth + 0.1
            && labelOrigin.Y + label.ActualHeight <= progress.ActualHeight + 0.1,
            "Progress percentage extends outside the bar.");
        Assert(label.Text == "42%", "Progress percentage text is not rendered by the bar template.");
    }

    private static void ValidateMainCommandFlows()
    {
        using var scope = new WindowScope(new MainWindow());
        var quickAction = Require<Button>(scope.Window, "QuickActionButton");
        var quickHeaders = quickAction.ContextMenu?.Items
            .OfType<MenuItem>()
            .Select(item => item.Header?.ToString())
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .ToArray()
            ?? [];
        Assert(
            quickHeaders.Contains("Send PC files to phone Downloads..."),
            "Quick action menu does not expose file send to phone Downloads.");
        Assert(
            quickHeaders.Contains("Send PC folder to phone Downloads..."),
            "Quick action menu does not expose folder send to phone Downloads.");
        Assert(
            quickHeaders.Contains("Download selected to PC Downloads"),
            "Quick action menu does not expose download to PC Downloads.");

        var tempRoot = Path.Combine(Path.GetTempPath(), $"PhoneTransferUi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var filePath = Path.Combine(tempRoot, "send-file.txt");
            var folderPath = Path.Combine(tempRoot, "send-folder");
            File.WriteAllText(filePath, "send test");
            Directory.CreateDirectory(folderPath);

            var mainWindow = (MainWindow)scope.Window;
            mainWindow.HandleStartupArgs([
                "--send-to-phone",
                "--mode",
                "online",
                filePath,
                folderPath
            ]);

            var pendingPaths = (List<string>)typeof(MainWindow)
                .GetField(
                    "_pendingSendToPhonePaths",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(mainWindow)!;
            var pendingMode = (string)typeof(MainWindow)
                .GetField(
                    "_pendingSendMode",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(mainWindow)!;
            var status = Require<TextBlock>(scope.Window, "OperationStatusText").Text;
            Assert(pendingPaths.Count == 2, "Startup send-to-phone did not queue both file and folder.");
            Assert(pendingMode == "online", "Startup send-to-phone did not preserve online mode.");
            Assert(
                status.Contains("phone Downloads", StringComparison.OrdinalIgnoreCase),
                "Startup send-to-phone status does not tell the user where items will go.");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static void ValidateCommandBridge()
    {
        var previousScope = Environment.GetEnvironmentVariable("PHONEFOLDER_INSTANCE_SCOPE");
        Environment.SetEnvironmentVariable("PHONEFOLDER_INSTANCE_SCOPE", Guid.NewGuid().ToString("N"));
        try
        {
            var received = new TaskCompletionSource<IReadOnlyList<string>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Assert(
                AppCommandBridge.TryCreatePrimary(
                    args => received.TrySetResult(args.ToArray()),
                    out var bridge),
                "The app command bridge did not create a primary instance.");
            using (bridge!)
            {
                Assert(
                    !AppCommandBridge.TryCreatePrimary(_ => { }, out var secondBridge),
                    "The app command bridge allowed a second primary instance.");
                secondBridge?.Dispose();

                var sent = AppCommandBridge.TrySendAsync(
                        ["--send-to-phone", @"C:\Temp\sample.txt"],
                        TimeSpan.FromSeconds(3))
                    .GetAwaiter()
                    .GetResult();
                Assert(sent, "The app command bridge did not accept forwarded arguments.");
                Assert(
                    received.Task.Wait(TimeSpan.FromSeconds(3)),
                    "The app command bridge did not deliver forwarded arguments.");

                var args = received.Task.Result;
                Assert(
                    args.SequenceEqual(["--send-to-phone", @"C:\Temp\sample.txt"]),
                    "The app command bridge changed forwarded arguments.");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHONEFOLDER_INSTANCE_SCOPE", previousScope);
        }
    }

    private static double AssertToolbarLayout(
        Panel panel,
        string label)
    {
        var buttons = panel.Children.OfType<Button>().ToArray();
        Assert(buttons.Length > 0, $"{label} contains no buttons.");
        Assert(
            FindVisualAncestor<ScrollViewer>(panel) is null,
            $"{label} is still inside a horizontal scrolling container.");

        foreach (var button in buttons)
        {
            Assert(
                Math.Abs(button.ActualHeight - 34) < 0.75,
                $"{label}: {button.NameOrContent()} height changed to {button.ActualHeight:0.##}.");
            Assert(
                button.ActualWidth >= 24,
                $"{label}: {button.NameOrContent()} became unusably narrow at {button.ActualWidth:0.##}.");
            var origin = button.TranslatePoint(new Point(0, 0), panel);
            Assert(origin.X >= 0 && origin.Y >= 0, $"{label}: a button is outside its panel.");
            Assert(
                origin.X + button.ActualWidth <= panel.ActualWidth + 0.1,
                $"{label}: {button.NameOrContent()} extends beyond the panel.");
        }

        var rowTop = buttons[0].TranslatePoint(new Point(0, 0), panel).Y;
        Assert(
            buttons.All(button =>
                Math.Abs(button.TranslatePoint(new Point(0, 0), panel).Y - rowTop) < 0.1),
            $"{label}: buttons wrapped onto more than one row.");

        for (var first = 0; first < buttons.Length; first++)
        {
            var firstOrigin = buttons[first].TranslatePoint(new Point(0, 0), panel);
            var firstBounds = new Rect(
                firstOrigin,
                new Size(buttons[first].ActualWidth, buttons[first].ActualHeight));
            for (var second = first + 1; second < buttons.Length; second++)
            {
                var secondOrigin = buttons[second].TranslatePoint(new Point(0, 0), panel);
                var secondBounds = new Rect(
                    secondOrigin,
                    new Size(buttons[second].ActualWidth, buttons[second].ActualHeight));
                Assert(
                    !firstBounds.IntersectsWith(secondBounds),
                    $"{label}: {buttons[first].NameOrContent()} overlaps {buttons[second].NameOrContent()}.");
            }
        }

        return buttons.Average(button => button.ActualWidth);
    }

    private static void AssertDarkWindowChrome(Window window, string label)
    {
        Assert(
            window.WindowStyle == WindowStyle.None,
            $"{label} still uses the light native title bar.");
        Assert(
            window.Background is SolidColorBrush background
            && background.Color.R < 40
            && background.Color.G < 40
            && background.Color.B < 40,
            $"{label} does not use a clean dark background.");
        Assert(
            window.Template.FindName("WindowFrame", window) is Border,
            $"{label} custom dark window frame is missing.");
    }

    private static void AssertDisabled(FrameworkElement root, string name)
    {
        var button = Require<Button>(root, name);
        Assert(!button.IsEnabled, $"{name} should be disabled without a selection.");
    }

    private static void AssertEnabled(FrameworkElement root, string name)
    {
        var button = Require<Button>(root, name);
        Assert(button.IsEnabled, $"{name} should be enabled for the current selection.");
    }

    private static T Require<T>(FrameworkElement root, string name)
        where T : FrameworkElement =>
        root.FindName(name) as T
        ?? throw new InvalidOperationException($"{name} was not found as {typeof(T).Name}.");

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }
            if (FindVisualChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }
        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject child)
        where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static void Layout(Window window, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        window.ApplyTemplate();
        if (window.Content is not FrameworkElement content)
        {
            throw new InvalidOperationException("Window content is not a framework element.");
        }
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
    }

    private static void RenderWindowContent(Window window, string path)
    {
        if (window.Content is not FrameworkElement content)
        {
            throw new InvalidOperationException("Window content is not renderable.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(content.ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(content.ActualHeight)),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string NameOrContent(this Button button) =>
        string.IsNullOrWhiteSpace(button.Name)
            ? button.Content?.ToString() ?? "button"
            : button.Name;

    private sealed class WindowScope(Window window) : IDisposable
    {
        public Window Window { get; } = window;

        public void Dispose() => Window.Close();
    }
}
