using PhoneFolder.Desktop.Models;
using PhoneFolder.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace PhoneFolder.Desktop;

public partial class MoveWindow : Window
{
    private readonly RemoteClient _client;
    private readonly HashSet<string> _excludedIds;
    private readonly ObservableCollection<FolderNode> _roots = [];

    private MoveWindow(
        RemoteClient client,
        RemoteItem root,
        IEnumerable<string> excludedIds,
        string action)
    {
        InitializeComponent();
        _client = client;
        _excludedIds = excludedIds.ToHashSet(StringComparer.Ordinal);
        Title = $"{action} to folder";
        InstructionText.Text = $"Choose where to {action.ToLowerInvariant()} the selected item(s)";
        ConfirmButton.Content = $"{action} here";
        _roots.Add(FolderNode.Create(root.Id, root.Name));
        FolderTree.DataContext = _roots;
        Loaded += async (_, _) => await LoadChildrenAsync(_roots[0]);
    }

    public FolderNode? SelectedFolder { get; private set; }

    public static FolderNode? Choose(
        Window owner,
        RemoteClient client,
        RemoteItem root,
        IEnumerable<string> excludedIds,
        string action = "Move")
    {
        var window = new MoveWindow(client, root, excludedIds, action) { Owner = owner };
        return window.ShowDialog() == true ? window.SelectedFolder : null;
    }

    private async void FolderTreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { DataContext: FolderNode node })
        {
            await LoadChildrenAsync(node);
        }
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        SelectedFolder = e.NewValue as FolderNode;
        var valid = SelectedFolder is not null
            && !SelectedFolder.IsPlaceholder
            && !SelectedFolder.Path().Any(node => _excludedIds.Contains(node.Id));
        ConfirmButton.IsEnabled = valid;
        DestinationText.Text = valid
            ? string.Join(" > ", SelectedFolder!.Path().Select(node => node.Name))
            : "Choose a different folder";
    }

    private async Task LoadChildrenAsync(FolderNode node)
    {
        if (node.IsLoaded || node.IsPlaceholder)
        {
            return;
        }

        try
        {
            var children = await _client.GetChildrenAsync(node.Id);
            node.Children.Clear();
            foreach (var folder in children
                         .Where(item => item.IsDirectory)
                         .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                node.Children.Add(FolderNode.Create(folder.Id, folder.Name, node));
            }
            node.IsLoaded = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Phone Transfer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedFolder is null
            || SelectedFolder.Path().Any(node => _excludedIds.Contains(node.Id)))
        {
            return;
        }
        DialogResult = true;
    }
}
