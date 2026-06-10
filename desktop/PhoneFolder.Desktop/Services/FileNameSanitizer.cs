using System.IO;

namespace PhoneFolder.Desktop.Services;

public static class FileNameSanitizer
{
    private const int MaxSegmentLength = 180;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "CLOCK$",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    };

    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(
                name.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim()
            .TrimEnd('.');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "download";
        }

        if (ReservedNames.Contains(Path.GetFileNameWithoutExtension(sanitized)))
        {
            sanitized = $"_{sanitized}";
        }

        if (sanitized.Length <= MaxSegmentLength)
        {
            return sanitized;
        }

        var extension = Path.GetExtension(sanitized);
        if (extension.Length >= MaxSegmentLength / 2)
        {
            extension = extension[..(MaxSegmentLength / 2)];
        }

        return sanitized[..(MaxSegmentLength - extension.Length)] + extension;
    }
}
