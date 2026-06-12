using PhoneFolder.Desktop.Models;

namespace PhoneFolder.Desktop.Services;

public static class RemoteClipboard
{
    private static IReadOnlyList<RemoteItem> _items = [];
    private static string _connectionKey = string.Empty;
    private static string _deviceName = string.Empty;

    public static IReadOnlyList<RemoteItem> Items => _items;
    public static bool IsCut { get; private set; }
    public static bool HasItems => _items.Count > 0;

    public static void Set(RemoteClient client, IReadOnlyList<RemoteItem> items, bool cut) =>
        Set(client.ConnectionKey, client.DeviceName, items, cut);

    public static void Set(
        string connectionKey,
        string deviceName,
        IReadOnlyList<RemoteItem> items,
        bool cut)
    {
        _items = items.ToArray();
        _connectionKey = connectionKey;
        _deviceName = deviceName;
        IsCut = cut;
    }

    public static void Clear()
    {
        _items = [];
        _connectionKey = string.Empty;
        _deviceName = string.Empty;
        IsCut = false;
    }

    public static async Task PasteAsync(
        RemoteClient client,
        string destinationId,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                client.ConnectionKey,
                _connectionKey,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"These items were copied from {_deviceName}. "
                + "Cross-phone paste is not supported; download them first, then upload them to this phone.");
        }

        foreach (var item in _items)
        {
            if (IsCut)
            {
                await client.MoveAsync(item.Id, destinationId, cancellationToken);
            }
            else
            {
                await client.CopyAsync(item.Id, destinationId, cancellationToken);
            }
        }
        if (IsCut)
        {
            Clear();
        }
    }
}
