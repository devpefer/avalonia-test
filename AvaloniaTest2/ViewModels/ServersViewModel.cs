using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using AvaloniaTest2.Models;
using CommunityToolkit.Mvvm.Input;

namespace AvaloniaTest2.ViewModels;

public class ServersViewModel
{
    public ObservableCollection<ServerItem> Servers { get; } = new();
    public ICommand AddServerCommand { get; }
    public ICommand RemoveServerCommand { get; }

    public ServersViewModel()
    {
        AddServerCommand = new AsyncRelayCommand(AddServerAsync);
        RemoveServerCommand = new RelayCommand<ServerItem>(RemoveServer);
    }

    private async Task AddServerAsync()
    {
        var window = new AvaloniaTest2.Views.AddServer();
        var vm = new AddServerViewModel(window);
        window.DataContext = vm;

        var result = await window.ShowDialog<ServerItem?>(App.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow!
            : null);

        if (result != null)
            Servers.Add(result);
    }

    private void RemoveServer(ServerItem? server)
    {
        if (server != null) Servers.Remove(server);
    }
}