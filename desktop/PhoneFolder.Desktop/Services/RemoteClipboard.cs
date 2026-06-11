using PhoneFolder.Desktop.Models;

namespace PhoneFolder.Desktop.Services;

public static class RemoteClipboard
{
    private static IReadOnlyList<RemoteItem> _items = [];

    public static IReadOnlyList<RemoteItem> Items => _items;
    public static bool IsCut { get; private set; }
    public static bool HasItems => _items.Count > 0;

    public static void Set(IReadOnlyList<RemoteItem> items, bool cut)
    {
        _items = items.ToArray();
        IsCut = cut;
    }

    public static void Clear()
    {
        _items = [];
        IsCut = false;
    }

    public static async Task PasteAsync(
        RemoteClient client,
        string destinationId,
        CancellationToken cancellationToken = default)
    {
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
