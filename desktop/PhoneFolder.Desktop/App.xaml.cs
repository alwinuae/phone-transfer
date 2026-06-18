using PhoneFolder.Desktop.Services;
using System.Windows;

namespace PhoneFolder.Desktop;

public partial class App : Application
{
    private AppCommandBridge? _commandBridge;
    private readonly List<IReadOnlyList<string>> _pendingExternalCommands = [];

    private static Window? GetWindow(object sender) =>
        sender is DependencyObject dependencyObject
            ? Window.GetWindow(dependencyObject)
            : null;

    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetWindow(sender) is { } window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private void MaximizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetWindow(sender) is { } window)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    private void RestoreWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetWindow(sender) is { } window)
        {
            window.WindowState = WindowState.Normal;
        }
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e) =>
        GetWindow(sender)?.Close();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        WindowCoordinator.Instance.Initialize(this);
        if (!AppCommandBridge.TryCreatePrimary(
                args => Dispatcher.BeginInvoke(() => HandleExternalCommand(args)),
                out _commandBridge)
            && AppCommandBridge.TrySendAsync(e.Args, TimeSpan.FromSeconds(3))
                .GetAwaiter()
                .GetResult())
        {
            Shutdown();
            return;
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
        mainWindow.HandleStartupArgs(e.Args);
        foreach (var command in _pendingExternalCommands.ToArray())
        {
            mainWindow.HandleStartupArgs(command);
        }
        _pendingExternalCommands.Clear();
    }

    private void HandleExternalCommand(IReadOnlyList<string> args)
    {
        if (MainWindow is MainWindow mainWindow)
        {
            mainWindow.HandleStartupArgs(args);
            return;
        }

        _pendingExternalCommands.Add(args.ToArray());
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _commandBridge?.Dispose();
        await DefaultMediaSessionManager.DisposeAsync();
        base.OnExit(e);
    }
}
