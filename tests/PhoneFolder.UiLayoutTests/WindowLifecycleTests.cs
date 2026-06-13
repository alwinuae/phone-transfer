using PhoneFolder.Desktop.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class WindowLifecycleTests
{
    public static void Run(Application application)
    {
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var coordinator = WindowCoordinator.Instance;
        coordinator.Initialize(application);
        ValidateCaptionCommands(coordinator);

        var main = CreateWindow("Lifecycle main");
        var firstFolder = CreateWindow("Lifecycle folder 1");
        var secondFolder = CreateWindow("Lifecycle folder 2");

        try
        {
            coordinator.ShowIndependent(main);
            coordinator.ShowIndependent(firstFolder);
            coordinator.ShowIndependent(secondFolder);

            Assert(main.Owner is null, "The main window unexpectedly has an owner.");
            Assert(firstFolder.Owner is null, "The first folder window unexpectedly has an owner.");
            Assert(secondFolder.Owner is null, "The second folder window unexpectedly has an owner.");

            secondFolder.Activate();
            PumpDispatcher();
            main.Activate();
            PumpDispatcher();
            main.Close();
            PumpDispatcher();

            Assert(firstFolder.IsVisible, "Closing the first window closed another folder window.");
            Assert(secondFolder.IsVisible, "Closing the first window closed the newest folder window.");
            Assert(
                ReferenceEquals(coordinator.MostRecentWindow, secondFolder),
                "The most recently used remaining Phone Transfer window was not selected.");
            Assert(
                secondFolder.IsActive,
                "The most recently used remaining Phone Transfer window was not activated.");
        }
        finally
        {
            if (main.IsVisible)
            {
                main.Close();
            }
            if (firstFolder.IsVisible)
            {
                firstFolder.Close();
            }
            if (secondFolder.IsVisible)
            {
                secondFolder.Close();
            }
            PumpDispatcher();
        }
    }

    private static void ValidateCaptionCommands(WindowCoordinator coordinator)
    {
        var window = CreateWindow("Caption command test");
        coordinator.ShowIndependent(window);
        PumpDispatcher();
        window.ApplyTemplate();

        var minimize = RequireCaptionButton(window, "MinimizeButton");
        var maximize = RequireCaptionButton(window, "MaximizeButton");
        var restore = RequireCaptionButton(window, "RestoreButton");
        var close = RequireCaptionButton(window, "CloseButton");

        Click(minimize);
        PumpDispatcher();
        Assert(window.WindowState == WindowState.Minimized, "The custom minimize button did not minimize its window.");

        window.WindowState = WindowState.Normal;
        PumpDispatcher();
        Click(maximize);
        PumpDispatcher();
        Assert(window.WindowState == WindowState.Maximized, "The custom maximize button did not maximize its window.");

        Click(restore);
        PumpDispatcher();
        Assert(window.WindowState == WindowState.Normal, "The custom restore button did not restore its window.");

        Click(close);
        PumpDispatcher();
        Assert(!window.IsVisible, "The custom close button did not close its window.");
    }

    private static Button RequireCaptionButton(Window window, string name) =>
        window.Template.FindName(name, window) as Button
        ?? throw new InvalidOperationException($"The custom title bar is missing {name}.");

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Window CreateWindow(string title) =>
        new()
        {
            Title = title,
            Width = 320,
            Height = 200,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
