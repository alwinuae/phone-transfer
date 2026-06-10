using System.Collections.ObjectModel;

namespace PhoneFolder.Desktop.Models;

public sealed class FolderNode
{
    private FolderNode(string id, string name, FolderNode? parent, bool placeholder)
    {
        Id = id;
        Name = name;
        Parent = parent;
        IsPlaceholder = placeholder;
    }

    public string Id { get; }
    public string Name { get; set; }
    public FolderNode? Parent { get; }
    public bool IsPlaceholder { get; }
    public bool IsLoaded { get; set; }
    public ObservableCollection<FolderNode> Children { get; } = [];

    public static FolderNode Create(string id, string name, FolderNode? parent = null)
    {
        var node = new FolderNode(id, name, parent, false);
        node.Children.Add(new FolderNode(string.Empty, "Loading...", node, true));
        return node;
    }

    public IReadOnlyList<FolderNode> Path()
    {
        var path = new List<FolderNode>();
        for (var node = this; node is not null; node = node.Parent)
        {
            path.Add(node);
        }
        path.Reverse();
        return path;
    }
}
