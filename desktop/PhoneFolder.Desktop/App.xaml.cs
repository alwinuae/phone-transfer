using PhoneFolder.Desktop.Services;
using System.Windows;

namespace PhoneFolder.Desktop;

public partial class App : Application
{
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
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
        mainWindow.HandleStartupArgs(e.Args);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await DefaultMediaSessionManager.DisposeAsync();
        base.OnExit(e);
    }
}
