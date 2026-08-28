using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PhoneFolder.Desktop.Services;

public static class DarkModeAssist
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(DarkModeAssist),
            new PropertyMetadata(false, EnabledChanged));

    public static void SetEnabled(DependencyObject element, bool value) =>
        element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(DependencyObject element) =>
        (bool)element.GetValue(EnabledProperty);

    /// <summary>
    /// Windows that always render dark content regardless of the app theme (e.g. the
    /// media viewer) set this so their title bar stays dark even in Light/System mode.
    /// </summary>
    public static readonly DependencyProperty ForceDarkProperty =
        DependencyProperty.RegisterAttached(
            "ForceDark",
            typeof(bool),
            typeof(DarkModeAssist),
            new PropertyMetadata(false));

    public static void SetForceDark(DependencyObject element, bool value) =>
        element.SetValue(ForceDarkProperty, value);

    public static bool GetForceDark(DependencyObject element) =>
        (bool)element.GetValue(ForceDarkProperty);

    private static void EnabledChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not Window window)
        {
            return;
        }

        window.SourceInitialized -= Window_SourceInitialized;
        if (args.NewValue is true)
        {
            window.SourceInitialized += Window_SourceInitialized;
        }
    }

    private static void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            Apply(window, GetForceDark(window) || ThemeService.CurrentIsDark);
        }
    }

    public static void Apply(Window window, bool isDark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var enabled = isDark ? 1 : 0;
        if (DwmSetWindowAttribute(
                handle,
                DwmUseImmersiveDarkMode,
                ref enabled,
                sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(
                handle,
                DwmUseImmersiveDarkModeBefore20H1,
                ref enabled,
                sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int value,
        int valueSize);
}
