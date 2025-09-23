using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace AvaloniaTest2.ViewModels;

public class AddServerViewModel
{
    public string? Name { get; set; }
    public string? Host { get; set; }
    public string? Port { get; set; }
    public string? Description { get; set; }

    public ICommand AcceptCommand { get; }
    public ICommand CancelCommand { get; }

    private readonly Window _window;

    public AddServerViewModel(Window window)
    {
        _window = window;
        AcceptCommand = new RelayCommand(Accept);
        CancelCommand = new RelayCommand(Cancel);
    }

    private void Accept()
    {
        _window.Close(new Models.ServerItem
        {
            Name = Name ?? "",
            Address = Host ?? "",
            Port = Port ?? "",
            Description = Description ?? ""
        });
    }

    private void Cancel() => _window.Close(null);
}