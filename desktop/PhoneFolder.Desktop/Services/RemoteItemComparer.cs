using PhoneFolder.Desktop.Models;
using System.Collections;
using System.Runtime.InteropServices;

namespace PhoneFolder.Desktop.Services;

public sealed class RemoteItemComparer(
    FileSortField field,
    bool descending) : IComparer
{
    public int Compare(object? x, object? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }
        if (x is not RemoteItem left)
        {
            return -1;
        }
        if (y is not RemoteItem right)
        {
            return 1;
        }

        var directoryOrder = right.IsDirectory.CompareTo(left.IsDirectory);
        if (directoryOrder != 0)
        {
            return directoryOrder;
        }

        var comparison = field switch
        {
            FileSortField.Modified => left.ModifiedAt.CompareTo(right.ModifiedAt),
            FileSortField.Type => StringComparer.CurrentCultureIgnoreCase.Compare(
                left.TypeLabel,
                right.TypeLabel),
            FileSortField.Size => left.Size.CompareTo(right.Size),
            _ => LogicalCompare(left.Name, right.Name)
        };
        if (comparison == 0)
        {
            comparison = LogicalCompare(left.Name, right.Name);
        }
        return descending ? -comparison : comparison;
    }

    private static int LogicalCompare(string left, string right) =>
        StrCmpLogicalW(left, right);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string left, string right);
}
