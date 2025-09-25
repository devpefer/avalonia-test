using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using AvaloniaTest2.Enums;
using AvaloniaTest2.Helpers;
using AvaloniaTest2.Interfaces;
using AvaloniaTest2.Models;
using AvaloniaTest2.Views;

namespace AvaloniaTest2.ViewModels;

public class FileExplorerViewModel : INotifyPropertyChanged
{
    private readonly IMessengerService _messengerService;
    private readonly HashSet<string> _visitedPaths = new(StringComparer.OrdinalIgnoreCase);

    private readonly string[] _blockedPaths = OperatingSystem.IsWindows()
        ? new[] { @"C:\Windows\WinSxS", @"C:\Windows\System32\config" }
        : Array.Empty<string>();

    private CancellationTokenSource? _cancellation;
    private int _activeSizeTasks;
    public ObservableCollection<FileSystemItem> RootItems { get; } = new();
    public ObservableCollection<DriveInfo> Drives { get; } = new();
    public Array SortModes => Enum.GetValues(typeof(SortMode));
    private FileSystemItem? _selectedItem;

    public FileSystemItem? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    private string _searchQuery = "";

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    private string? _currentItemBeingProcessed;

    public string? CurrentItemBeingProcessed
    {
        get => _currentItemBeingProcessed;
        private set => SetProperty(ref _currentItemBeingProcessed, value);
    }

    private bool _isCalculatingSizes;

    public bool IsCalculatingSizes
    {
        get => _isCalculatingSizes;
        private set => SetProperty(ref _isCalculatingSizes, value);
    }

    private SortMode _selectedSort = SortMode.SizeDesc;

    public SortMode SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (SetProperty(ref _selectedSort, value)) ApplySorting();
        }
    }

    public ICommand OpenFileCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand CopyPathCommand { get; }
    public ICommand MoveToTrashCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand CancelCommand { get; }
    public event Action? SizesCalculationCompleted;
    public event PropertyChangedEventHandler? PropertyChanged;

    public FileExplorerViewModel(IMessengerService messengerService)
    {
        _messengerService = messengerService;
        OpenFileCommand = new RelayCommand<FileSystemItem>(OpenFile);
        OpenFolderCommand = new RelayCommand<FileSystemItem>(OpenFolder);
        CopyPathCommand = new RelayCommand<FileSystemItem>(CopyPath);
        MoveToTrashCommand = new RelayCommand<FileSystemItem>(DeleteFileFromList);
        SearchCommand = new RelayCommand<string>(async q => await StartSearchAsync(q));
        CancelCommand = new RelayCommand<string>(CancelSearch);
        InitializeDrives();
    }

    private void InitializeDrives()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
                AddDriveRoot(d.Name, d.RootDirectory.FullName, d);
        }
        else AddDriveRoot("/", "/", null);
    }

    private void AddDriveRoot(string name, string path, DriveInfo? drive)
    {
        long size = -1;
        try
        {
            if (drive?.IsReady == true) size = drive.TotalSize;
        }
        catch
        {
        }

        var root = new FileSystemItem { Name = name, FullPath = path, IsDirectory = true, LogicalSize = size };
        root.Children.Add(new FileSystemItem { Name = "Cargando...", LogicalSize = -1 });
        RootItems.Add(root);
    }

// ===================== BUSQUEDA AVANZADA =====================
    private void CancelSearch(string _) => _cancellation?.Cancel();

    private async Task StartSearchAsync(string query)
    {
        CancelSearch(query);
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        RootItems.Clear();
        IsCalculatingSizes = true;
        CurrentItemBeingProcessed = null;
        var results = new ObservableCollection<FileSystemItem>();
        try
        {
            foreach (var drive in Drives)
            {
                string rootPath = OperatingSystem.IsWindows() ? drive.Name : "/";
                await Task.Run(async () => await SearchFilesRecursive(rootPath, query, results, token), token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var r in results) RootItems.Add(r);
                IsCalculatingSizes = false;
                CurrentItemBeingProcessed = null;
            });
        }
    }

    private async Task SearchFilesRecursive(string path, string query, ObservableCollection<FileSystemItem> results,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CurrentItemBeingProcessed = path;
        FileInfo[] files = Array.Empty<FileInfo>();
        DirectoryInfo[] dirs = Array.Empty<DirectoryInfo>();
        try
        {
            files = SafeGetFiles(new DirectoryInfo(path));
        }
        catch
        {
        }

        try
        {
            dirs = SafeGetDirectories(new DirectoryInfo(path));
        }
        catch
        {
        }

        foreach (var f in files)
        {
            token.ThrowIfCancellationRequested();
            if (Path.GetFileName(f.FullName).Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                var item = new FileSystemItem
                    { Name = f.Name, FullPath = f.FullName, IsDirectory = false, LogicalSize = f.Length };
                await Dispatcher.UIThread.InvokeAsync(() => results.Add(item));
            }
        }

        foreach (var d in dirs)
        {
            token.ThrowIfCancellationRequested();
            if (_visitedPaths.Add(d.FullName) &&
                !_blockedPaths.Any(bp => d.FullName.StartsWith(bp, StringComparison.OrdinalIgnoreCase)))
            {
                if ((d.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0) continue;
                await SearchFilesRecursive(d.FullName, query, results, token);
            }
        }
    } // ===================== EXPANDIR Y CALCULAR TAMAÑOS =====================

    public async Task LoadChildren(FileSystemItem parent)
    {
        if (!parent.IsDirectory) return;
        if (parent.Children.Count == 1 && parent.Children[0].Name == "Cargando...") parent.Children.Clear();
        else if (parent.Children.Count > 0) return;
        string[] files = Array.Empty<string>();
        string[] dirs = Array.Empty<string>();
        try
        {
            files = Directory.GetFiles(parent.FullPath);
        }
        catch
        {
        }

        try
        {
            dirs = Directory.GetDirectories(parent.FullPath);
        }
        catch
        {
        }

        foreach (var dir in dirs)
        {
            if (_blockedPaths.Any(bp => dir.StartsWith(bp, StringComparison.OrdinalIgnoreCase))) continue;
            DirectoryInfo di;
            try
            {
                di = new DirectoryInfo(dir);
            }
            catch
            {
                continue;
            }

            if ((di.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0) continue;
            var childDir = new FileSystemItem
                { Name = di.Name, FullPath = di.FullName, IsDirectory = true, LogicalSize = 0, Parent = parent };
            childDir.Children.Add(new FileSystemItem { Name = "Cargando...", LogicalSize = -1 });
            await Dispatcher.UIThread.InvokeAsync(() => parent.Children.Add(childDir));
            Interlocked.Increment(ref _activeSizeTasks);
            _ = Task.Run(() => CalculateDirectorySizeAsync(childDir));
        }

        foreach (var file in files)
        {
            if (_blockedPaths.Any(bp => file.StartsWith(bp, StringComparison.OrdinalIgnoreCase))) continue;
            FileInfo fi;
            try
            {
                fi = new FileInfo(file);
            }
            catch
            {
                continue;
            }

            if ((fi.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device | FileAttributes.System)) !=
                0) continue;
            var child = new FileSystemItem
            {
                Name = fi.Name, FullPath = fi.FullName, IsDirectory = false, LogicalSize = fi.Length, Parent = parent
            };
            await Dispatcher.UIThread.InvokeAsync(() => parent.Children.Add(child));
        }
    }

    private async Task CalculateDirectorySizeAsync(FileSystemItem dirItem)
    {
        try
        {
            dirItem.LogicalSize = await GetDirectorySizeSafeAsync(new DirectoryInfo(dirItem.FullPath));
            await UpdateParentSizesAsync(dirItem);
        }
        finally
        {
            if (Interlocked.Decrement(ref _activeSizeTasks) == 0)
            {
                ApplySorting();
                IsCalculatingSizes = false;
                CurrentItemBeingProcessed = null;
                SizesCalculationCompleted?.Invoke();
            }
        }
    }

    private async Task UpdateParentSizesAsync(FileSystemItem item)
    {
        if (item.Parent == null) return;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            item.Parent.LogicalSize = item.Parent.Children.Sum(c => c.LogicalSize > 0 ? c.LogicalSize : 0);
        });
        if (item.Parent.Parent != null) await UpdateParentSizesAsync(item.Parent);
    }

    private async Task<long> GetDirectorySizeSafeAsync(DirectoryInfo dir, int maxConcurrentTasks = 8)
    {
        long total = 0;
        FileInfo[] files = Array.Empty<FileInfo>();
        DirectoryInfo[] dirs = Array.Empty<DirectoryInfo>();
        try
        {
            files = dir.GetFiles();
        }
        catch
        {
        }

        try
        {
            dirs = dir.GetDirectories();
        }
        catch
        {
        }

        var semaphore = new SemaphoreSlim(maxConcurrentTasks);
        var tasks = new List<Task>();
        foreach (var f in files)
        {
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    if ((f.Attributes &
                         (FileAttributes.ReparsePoint | FileAttributes.Device | FileAttributes.System)) != 0) return;
                    if (_blockedPaths.Any(bp => f.FullName.StartsWith(bp, StringComparison.OrdinalIgnoreCase))) return;
                    Interlocked.Add(ref total, f.Length);
                    CurrentItemBeingProcessed = f.FullName;
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        foreach (var d in dirs)
        {
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    long subSize = 0;
                    try
                    {
                        subSize = await GetDirectorySizeSafeAsync(d, maxConcurrentTasks);
                    }
                    catch
                    {
                    }

                    Interlocked.Add(ref total, subSize);
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);
        return total;
    }

    private void ApplySorting()
    {
        foreach (var root in RootItems) SortRecursive(root, SelectedSort);
    }

    private void SortRecursive(FileSystemItem parent, SortMode sortMode)
    {
        if (!parent.Children.Any()) return;
        var sorted = sortMode switch
        {
            SortMode.SizeDesc => parent.Children.OrderByDescending(c => c.LogicalSize).ToList(),
            _ => parent.Children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList()
        };
        for (int i = 0; i < sorted.Count; i++)
        {
            int currentIndex = parent.Children.IndexOf(sorted[i]);
            if (currentIndex != i) parent.Children.Move(currentIndex, i);
        }

        foreach (var child in parent.Children.Where(c => c.IsDirectory)) SortRecursive(child, sortMode);
    }

        private FileInfo[] SafeGetFiles(DirectoryInfo dir)
    {
        try
        {
            return dir.GetFiles();
        }
        catch
        {
            return Array.Empty<FileInfo>();
        }
    }

    private DirectoryInfo[] SafeGetDirectories(DirectoryInfo dir)
    {
        try
        {
            return dir.GetDirectories();
        }
        catch
        {
            return Array.Empty<DirectoryInfo>();
        }
    }

    // ===================== COMANDOS =====================
    private void OpenFile(FileSystemItem? item)
    {
        if (item == null || item.IsDirectory) return;
        try
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _messengerService.ShowMessageDialog(null, $"No se pudo abrir el archivo:\n{ex.Message}");
        }
    }

    private async void OpenFolder(FileSystemItem? item)
    {
        if (item == null) return;
        string path = item.IsDirectory ? item.FullPath : Path.GetDirectoryName(item.FullPath)!;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await _messengerService.ShowMessageDialog(null, $"No se pudo abrir la carpeta:\n{ex.Message}");
        }
    }

    private async void CopyPath(FileSystemItem item)
    {
        if (item == null) return;
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                await desktop.MainWindow.Clipboard.SetTextAsync(item.FullPath);
            }
        }
        catch (Exception ex)
        {
            await _messengerService.ShowMessageDialog(null, $"No se pudo copiar la ruta:\n{ex.Message}");
        }
    }

    private async void DeleteFileFromList(FileSystemItem? item)
    {
        if (item == null) return;
        try
        {
            if (item.IsDirectory)
                Directory.Delete(item.FullPath, true);
            else
                File.Delete(item.FullPath);

            item.Parent?.Children.Remove(item);
            RootItems.Remove(item);
        }
        catch (Exception ex)
        {
            await _messengerService.ShowMessageDialog(null, $"No se pudo eliminar:\n{ex.Message}");
        }
    }

    // ===================== PROPERTYCHANGED =====================
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
