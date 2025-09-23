using System.Collections.ObjectModel;
using System.Windows.Input;
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
        AddServerCommand = new RelayCommand<ServerItem>(AddServer);
        RemoveServerCommand = new RelayCommand<ServerItem>(RemoveServer);
    }

    private void AddServer(ServerItem? server)
    {
        Servers.Add(server);
    }

    private void RemoveServer(ServerItem? server)
    {
        if (server != null) Servers.Remove(server);
    }

}