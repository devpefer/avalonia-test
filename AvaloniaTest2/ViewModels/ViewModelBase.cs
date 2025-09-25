using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AvaloniaTest2.ViewModels;

public class ViewModelBase : INotifyPropertyChanged
{
    private string _windowTitle = "";
    public string WindowTitle
    {
        get => _windowTitle;
        set
        {
            if (_windowTitle != value)
            {
                _windowTitle = value;
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }
    
    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}