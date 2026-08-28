using System.IO;
using System.Text.Json;

namespace PhoneFolder.Desktop.Services;

public static class AppSettingsStore
{
    private static string SettingsPath =>
        Environment.GetEnvironmentVariable("PHONEFOLDER_SETTINGS_PATH")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Phone Transfer",
            "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath))
                    ?? new AppSettings()
                : new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(
            SettingsPath,
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public sealed record AppSettings(
    bool AlwaysOpenInDefaultApplication = false,
    AppThemeMode Theme = AppThemeMode.Dark);

public enum AppThemeMode
{
    Dark,
    Light,
    System
}
