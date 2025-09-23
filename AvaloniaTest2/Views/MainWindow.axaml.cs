using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaTest2.ViewModels;

namespace AvaloniaTest2.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();

        this.Opened += (_, _) =>
        {
            if (FilesListBox?.ContextMenu != null)
                FilesListBox.ContextMenu.DataContext = DataContext;
        };
    }

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed && sender is Control ctrl && FilesListBox != null)
        {
            FilesListBox.SelectedItem = ctrl.DataContext;
        }
    }
}
