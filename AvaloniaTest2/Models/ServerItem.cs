using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AvaloniaTest2.Models;

public class ServerItem : INotifyPropertyChanged
{
    public string Name { get; set; }
    public string Host { get; set; }
    public string Port { get; set; }
    public string Description { get; set; }
    public string Status { get; set; }

    private bool _isOnline;
    public bool IsOnline
    {
        get => _isOnline;
        set { _isOnline = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
