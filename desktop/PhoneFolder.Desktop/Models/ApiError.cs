namespace PhoneFolder.Desktop.Models;

public sealed class ApiError
{
    public string Code { get; set; } = "UNKNOWN";
    public string Message { get; set; } = "The phone returned an error.";
}
