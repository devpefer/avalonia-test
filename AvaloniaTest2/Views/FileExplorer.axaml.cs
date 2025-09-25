using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaTest2.Models;
using AvaloniaTest2.Services;
using AvaloniaTest2.ViewModels;

namespace AvaloniaTest2.Views;

public partial class FileExplorer : UserControl
{
    public FileExplorer()
    {
        InitializeComponent();
        DataContext = new FileExplorerViewModel(new MessengerService());
        Tree.AddHandler(TreeViewItem.ExpandedEvent, TreeViewItem_Expanded, RoutingStrategies.Bubble);
        Tree.SelectionChanged += (s, e) =>
        {
            if (DataContext is FileExplorerViewModel vm)
            {
                vm.SelectedItem = Tree.SelectedItem as FileSystemItem;
            }
        };
    }

    private async Task TreeViewItem_Expanded(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TreeViewItem item && item.DataContext is FileSystemItem fsi)
        {
            if (fsi.Children.Count == 1 && fsi.Children[0].Name == "Cargando...")
            {
                ((FileExplorerViewModel)DataContext!).LoadChildren(fsi);
            }
        }
    }
}