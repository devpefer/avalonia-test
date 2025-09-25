using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaTest2.ViewModels;

namespace AvaloniaTest2.Views;

public partial class FloatingPanel : Window
{
    private readonly FileExplorerViewModel? _vm;
    private Point _dragStart;

    public FloatingPanel()
    {
        InitializeComponent();

        this.Position = new PixelPoint(
            (int)(Screens.Primary.Bounds.Width - this.Width - 20),
            20);

        if (Application.Current.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            _vm = desktop.MainWindow?.DataContext as FileExplorerViewModel;
            if (_vm != null)
            {
                this.DataContext = _vm;
            }
        }

        this.PointerPressed += OnPointerPressed;
        this.PointerMoved += OnPointerMoved;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            _dragStart = e.GetPosition(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var current = e.GetPosition(this);
            var dx = current.X - _dragStart.X;
            var dy = current.Y - _dragStart.Y;

            this.Position = new PixelPoint(
                (int)(this.Position.X + dx),
                (int)(this.Position.Y + dy));
        }
    }

    private void OpenMainWindow_Click(object? sender, RoutedEventArgs e)
    {
        if (Application.Current.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = desktop.MainWindow;
            mainWindow?.Show();
            mainWindow?.Activate();
        }
    }

    private void ExitApp_Click(object? sender, RoutedEventArgs e)
    {
        if (Application.Current.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
