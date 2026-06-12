namespace PhoneFolder.Desktop.Models;

public sealed record RemoteDragPayload(
    string ConnectionKey,
    string DeviceName,
    RemoteItem[] Items);
