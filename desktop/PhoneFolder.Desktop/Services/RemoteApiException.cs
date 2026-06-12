using System.Net;

namespace PhoneFolder.Desktop.Services;

public sealed class RemoteApiException(
    HttpStatusCode statusCode,
    string code,
    string message) : InvalidOperationException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
