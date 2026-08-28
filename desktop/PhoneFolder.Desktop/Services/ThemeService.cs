using Microsoft.Win32;
using System.Windows;

namespace PhoneFolder.Desktop.Services;

public static class ThemeService
{
    private const string DarkDictionaryUri = "Themes/Theme.Dark.xaml";
    private const string LightDictionaryUri = "Themes/Theme.Light.xaml";

    public static bool CurrentIsDark { get; private set; } = true;

    public static void Apply(AppThemeMode mode)
    {
        var useDark = ResolveIsDark(mode);
        CurrentIsDark = useDark;

        var resources = Application.Current.Resources;
        var stale = resources.MergedDictionaries
            .Where(dictionary => dictionary.Source is { OriginalString: DarkDictionaryUri or LightDictionaryUri })
            .ToArray();
        foreach (var dictionary in stale)
        {
            resources.MergedDictionaries.Remove(dictionary);
        }

        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(useDark ? DarkDictionaryUri : LightDictionaryUri, UriKind.Relative)
        });

        foreach (Window window in Application.Current.Windows)
        {
            DarkModeAssist.Apply(window, DarkModeAssist.GetForceDark(window) || useDark);
        }
    }

    private static bool ResolveIsDark(AppThemeMode mode) => mode switch
    {
        AppThemeMode.Dark => true,
        AppThemeMode.Light => false,
        _ => DetectSystemIsDark()
    };

    private static bool DetectSystemIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int appsUseLightTheme)
            {
                return appsUseLightTheme == 0;
            }
        }
        catch
        {
            // Registry read failed (older Windows, policy lockdown, etc.) - default to dark below.
        }
        return true;
    }
}
