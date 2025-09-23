using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaTest2.Models;
using AvaloniaTest2.ViewModels;

namespace AvaloniaTest2.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
        Tree.AddHandler(TreeViewItem.ExpandedEvent, TreeViewItem_Expanded, RoutingStrategies.Bubble);

        // this.Opened += (_, _) =>
        // {
        //     if (FilesListBox?.ContextMenu != null)
        //         FilesListBox.ContextMenu.DataContext = DataContext;
        // };
    }
    
    private void TreeViewItem_Expanded(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TreeViewItem item && item.DataContext is FileSystemItem fsi)
        {
            // Si tiene solo el marcador "Cargando...", entonces cargamos de verdad
            if (fsi.Children.Count == 1 && fsi.Children[0].Name == "Cargando...")
            {
                ((MainWindowViewModel)DataContext!).LoadChildren(fsi);
            }
        }
    }
    
    // private async void TreeView_Expanded(object sender, RoutedEventArgs e)
    // {
    //     if (e.Source is TreeViewItem item && item.DataContext is FileSystemItem fsi)
    //     {
    //         if (fsi.Children.Count == 1 && fsi.Children[0].Name == "Cargando...")
    //         {
    //             await ((MainWindowViewModel)DataContext).ExpandItemAsync(fsi);
    //         }
    //     }
    // }

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed && sender is Control ctrl )//&& FilesListBox != null)
        {
            //FilesListBox.SelectedItem = ctrl.DataContext;
        }
    }
}
