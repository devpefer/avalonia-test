using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaTest2;

public class SearchResultsViewModel : INotifyPropertyChanged
{
    public ObservableCollection<string> Results { get; } = new();

    private string? _currentFile;
    public string? CurrentFile
    {
        get => _currentFile;
        private set
        {
            if (_currentFile != value)
            {
                _currentFile = value;
                OnPropertyChanged();
            }
        }
    }

    private int _progress;
    public int Progress
    {
        get => _progress;
        private set
        {
            if (_progress != value)
            {
                _progress = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isSearching;
    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (_isSearching != value)
            {
                _isSearching = value;
                OnPropertyChanged();
            }
        }
    }

    private readonly Window _window;
    public ICommand CloseCommand { get; }
    
    private static readonly string[] IgnoredFolders = new[]
    {
        "/proc",
        "/sys",
        "/dev",
        "/run",
        "/tmp",
        "/var/lib",
        "/var/run",
    };


    public SearchResultsViewModel(Window window)
    {
        _window = window;
        CloseCommand = new RelayCommand<string>(Close);
    }

    public async Task StartSearchAsync(string fileName)
    {
        Results.Clear();
        CurrentFile = null;
        Progress = 0;
        IsSearching = true;

        var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
        int totalDrives = drives.Count;
        int processedDrives = 0;

        foreach (var drive in drives)
        {
            await Task.Run(() => SearchDirectory(drive.RootDirectory.FullName, fileName));

            processedDrives++;
            Dispatcher.UIThread.Post(() =>
            {
                Progress = (int)((processedDrives / (double)totalDrives) * 100);
            });
        }

        IsSearching = false;
        CurrentFile = null;
    }
    
    private void SearchDirectory(string directory, string fileName)
    {
        // Ignorar carpetas prohibidas
        if (IgnoredFolders.Any(f => directory.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
            return;

        try
        {
            foreach (var file in Directory.GetFiles(directory, fileName))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!Results.Contains(file))
                    {
                        Results.Add(file);
                    }

                    CurrentFile = file;
                });
                
            }

            foreach (var subDir in Directory.GetDirectories(directory))
            {
                SearchDirectory(subDir, fileName);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private void Close(string ruta) => _window.Close();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
