using System.Windows;
using System.Windows.Threading;

namespace PhoneFolder.Desktop.Services;

public sealed class WindowCoordinator
{
    private readonly List<Window> _activationOrder = [];
    private Application? _application;

    public static WindowCoordinator Instance { get; } = new();

    public Window? MostRecentWindow =>
        _activationOrder.LastOrDefault(window => window.IsLoaded && window.IsVisible);

    public void Initialize(Application application)
    {
        if (ReferenceEquals(_application, application))
        {
            return;
        }

        if (_application is not null)
        {
            _application.Activated -= Application_Activated;
        }

        _application = application;
        _application.Activated += Application_Activated;
        RegisterOwnerlessWindows();
    }

    public void ShowIndependent(Window window)
    {
        if (window.Owner is not null)
        {
            throw new InvalidOperationException(
                "Independent Phone Transfer windows cannot have an owner.");
        }

        if (window.WindowStartupLocation == WindowStartupLocation.CenterOwner)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Register(window);
        window.Show();
        Touch(window);
        Activate(window);
    }

    public T ShowSingleton<T>(Func<T> createWindow)
        where T : Window
    {
        var existing = _activationOrder
            .OfType<T>()
            .LastOrDefault(window => window.IsLoaded);
        if (existing is null)
        {
            existing = createWindow();
            ShowIndependent(existing);
            return existing;
        }

        if (!existing.IsVisible)
        {
            existing.Show();
        }
        if (existing.WindowState == WindowState.Minimized)
        {
            existing.WindowState = WindowState.Normal;
        }

        Touch(existing);
        Activate(existing);
        return existing;
    }

    private void Application_Activated(object? sender, EventArgs e)
    {
        RegisterOwnerlessWindows();
        if (_application?.Windows.OfType<Window>()
                .FirstOrDefault(window => window.IsActive && window.Owner is null) is { } active)
        {
            Touch(active);
        }
    }

    private void RegisterOwnerlessWindows()
    {
        if (_application is null)
        {
            return;
        }

        foreach (Window window in _application.Windows)
        {
            if (window.Owner is null)
            {
                Register(window);
            }
        }
    }

    private void Register(Window window)
    {
        if (_activationOrder.Contains(window))
        {
            return;
        }

        _activationOrder.Add(window);
        window.Activated += Window_Activated;
        window.Closed += Window_Closed;
    }

    private void Window_Activated(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            Touch(window);
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (sender is not Window closedWindow)
        {
            return;
        }

        closedWindow.Activated -= Window_Activated;
        closedWindow.Closed -= Window_Closed;
        _activationOrder.Remove(closedWindow);

        closedWindow.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(
                () =>
                {
                    if (MostRecentWindow is { } remainingWindow)
                    {
                        Activate(remainingWindow);
                    }
                }));
    }

    private void Touch(Window window)
    {
        _activationOrder.Remove(window);
        _activationOrder.Add(window);
    }

    private static void Activate(Window window)
    {
        if (!window.IsVisible)
        {
            return;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        window.Activate();
    }
}
