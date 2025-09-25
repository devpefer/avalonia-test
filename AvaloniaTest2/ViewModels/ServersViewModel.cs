using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls.ApplicationLifetimes;
using AvaloniaTest2.Models;
using CommunityToolkit.Mvvm.Input;

namespace AvaloniaTest2.ViewModels;

public class ServersViewModel : ViewModelBase
{
    public ObservableCollection<ServerItem> Servers { get; } = new();
    public ICommand AddServerCommand { get; }
    public ICommand RemoveServerCommand { get; }

    private CancellationTokenSource? _pingCts;
    private int _pingIntervalMinutes = 10; // Por defecto 1 minuto

    public int PingIntervalMinutes
    {
        get => _pingIntervalMinutes;
        set
        {
            if (value <= 0) return;
            _pingIntervalMinutes = value;
            RestartPingLoop();
        }
    }

    public ServersViewModel()
    {
        AddServerCommand = new AsyncRelayCommand(AddServerAsync);
        RemoveServerCommand = new RelayCommand<ServerItem>(RemoveServer);
        StartPingLoop();
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

    private void StartPingLoop()
    {
        _pingCts = new CancellationTokenSource();
        _ = PingLoopAsync(_pingCts.Token);
    }

    private void RestartPingLoop()
    {
        _pingCts?.Cancel();
        StartPingLoop();
    }

    private async Task PingLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            foreach (var server in Servers)
            {
                server.Status = "Pinging...";
                try
                {
                    // using var ping = new Ping();
                    // var reply = await ping.SendPingAsync(server.Host, 2000);
                    // server.IsOnline = reply.Status == IPStatus.Success;
                    server.IsOnline = await PingHostAsync(server.Host);
                }
                catch (Exception ex)
                {
                    server.Status = "Error";
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(PingIntervalMinutes), token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
    
    private async Task<bool> PingHostAsync(string host, int port = 80, int timeout = 2000)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var task = client.ConnectAsync(host, port);
            var result = await Task.WhenAny(task, Task.Delay(timeout));
            return result == task && client.Connected;
        }
        catch
        {
            return false;
        }
    }

}
