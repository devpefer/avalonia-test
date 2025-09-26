using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AvaloniaTest2;
using AvaloniaTest2.Services;
using AvaloniaTest2.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    public FileExplorerViewModel FileExplorerVm { get; }
    public ServersViewModel ServersVm { get; }
    public ServiceBrokerViewModel ServiceBrokerVm { get; }

    public ObservableCollection<string> MenuItems { get; } = new()
    {
        "Explorador de Archivos",
        "Servidores",
        "Service Broker"
    };

    private string _selectedMenuItem = "Explorador de Archivos";
    public string SelectedMenuItem
    {
        get => _selectedMenuItem;
        set
        {
            if (_selectedMenuItem != value)
            {
                _selectedMenuItem = value;
                OnPropertyChanged();
                UpdateSelectedView();
            }
        }
    }

    private object _selectedView;
    public object SelectedView
    {
        get => _selectedView;
        set
        {
            _selectedView = value;
            OnPropertyChanged();
        }
    }

    // Control de visibilidad del menú
    private bool _menuVisible = true;
    public bool MenuVisible
    {
        get => _menuVisible;
        set { _menuVisible = value; OnPropertyChanged(); }
    }

    public ICommand ToggleMenuCommand { get; }

    public MainWindowViewModel()
    {
        ToggleMenuCommand = new RelayCommand<object?>(_ => MenuVisible = !MenuVisible);
        FileExplorerVm = new FileExplorerViewModel(
            new MessengerService(),
            new InAppNotifier()
        );
        ServersVm = new ServersViewModel();
        ServiceBrokerVm = new ServiceBrokerViewModel();
        UpdateSelectedView();
    }

    private void UpdateSelectedView()
    {
        SelectedView = _selectedMenuItem switch
        {
            "Explorador de Archivos" => FileExplorerVm,
            "Servidores" => ServersVm,
            "Service Broker" => ServiceBrokerVm,
            _ => null
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}