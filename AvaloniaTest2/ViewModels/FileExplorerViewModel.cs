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
    private int _activeSizeTasks = 0;

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
            if (SetProperty(ref _selectedSort, value))
                ApplySorting();
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
            foreach (var d in DriveInfo.GetDrives())
            {
                if (!d.IsReady) continue;
                AddDriveRoot(d.Name, d.RootDirectory.FullName, d);
            }
        }
        else
        {
            AddDriveRoot("/", "/", null);
        }
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

        var root = new FileSystemItem
        {
            Name = name,
            FullPath = path,
            IsDirectory = true,
            LogicalSize = size
        };
        root.Children.Add(new FileSystemItem { Name = "Cargando...", LogicalSize = -1 });
        RootItems.Add(root);
    }

    // ===================== BUSQUEDA AVANZADA =====================
    private void CancelSearch(string file)
    {
        _cancellation?.Cancel();
    }

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
                await Task.Run(async () => { await SearchFilesRecursive(rootPath, query, results, token); }, token);
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

        FileInfo[] files = [];
        DirectoryInfo[] dirs = [];

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
                var fi = new FileInfo(f.FullName);
                var item = new FileSystemItem
                {
                    Name = fi.Name,
                    FullPath = fi.FullName,
                    IsDirectory = false,
                    LogicalSize = fi.Length
                };
                await Dispatcher.UIThread.InvokeAsync(() => results.Add(item));
            }
        }

        foreach (var d in dirs)
        {
            token.ThrowIfCancellationRequested();
            if (_visitedPaths.Add(d.FullName) && !_blockedPaths.Any(bp => d.FullName.StartsWith(bp, StringComparison.OrdinalIgnoreCase)))
            {
                var di = new DirectoryInfo(d.FullName);
                if ((di.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0) continue;
                await SearchFilesRecursive(d.FullName, query, results, token);
            }
        }
    }

    // ===================== EXPANDIR Y CALCULAR TAMAÑOS =====================
    public async Task LoadChildren(FileSystemItem parent)
    {
        if (!parent.IsDirectory) return;

        // Si ya cargamos previamente, no lo hacemos otra vez
        if (parent.Children.Count == 1 && parent.Children[0].Name == "Cargando...")
            parent.Children.Clear();
        else if (parent.Children.Count > 0)
            return;

        await Task.Run(async () =>
        {
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
                {
                    Name = di.Name,
                    FullPath = di.FullName,
                    IsDirectory = true,
                    LogicalSize = 0,
                    Parent = parent
                };

                // Añadimos un hijo "Cargando..." para que sea expandible
                childDir.Children.Add(new FileSystemItem { Name = "Cargando...", LogicalSize = -1 });

                await Dispatcher.UIThread.InvokeAsync(() => parent.Children.Add(childDir));

                Interlocked.Increment(ref _activeSizeTasks);
                _ = CalculateDirectorySizeAsync(childDir);
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
                    Name = fi.Name,
                    FullPath = fi.FullName,
                    IsDirectory = false,
                    LogicalSize = fi.Length,
                    Parent = parent
                };

                await Dispatcher.UIThread.InvokeAsync(() => parent.Children.Add(child));
            }
        });
    }

    private async Task CalculateDirectorySizeAsync(FileSystemItem dirItem)
    {
        try
        {
            var di = new DirectoryInfo(dirItem.FullPath);
            long size = await GetDirectorySizeSafeAsync(di);
            dirItem.LogicalSize = size;
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
        if (item.Parent.Parent != null)
            await UpdateParentSizesAsync(item.Parent);
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
                    CurrentItemBeingProcessed = f.FullName;
                    Interlocked.Add(ref total, f.Length);
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
                    long subdirSize = 0;
                    try
                    {
                        subdirSize = await GetDirectorySizeSafeAsync(d, maxConcurrentTasks);
                    }
                    catch
                    {
                        // Ignorar directorios inaccesibles
                    }
                    Interlocked.Add(ref total, subdirSize);
                }
                finally { semaphore.Release(); }
            }));
        }


        await Task.WhenAll(tasks);
        return total;
    }

    private void ApplySorting()
    {
        foreach (var root in RootItems)
            SortRecursive(root, SelectedSort);
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
            if (currentIndex != i)
                parent.Children.Move(currentIndex, i);
        }

        foreach (var child in parent.Children.Where(c => c.IsDirectory))
            SortRecursive(child, sortMode);
    }
    
    private FileInfo[] SafeGetFiles(DirectoryInfo dir)
    {
        try { return dir.GetFiles(); }
        catch { return Array.Empty<FileInfo>(); }
    }

    private DirectoryInfo[] SafeGetDirectories(DirectoryInfo dir)
    {
        try { return dir.GetDirectories(); }
        catch { return Array.Empty<DirectoryInfo>(); }
    }

    
    // private FileInfo[] SafeGetFiles(DirectoryInfo dir)
    // {
    //     try { return dir.GetFiles(); }
    //     catch (UnauthorizedAccessException) { return Array.Empty<FileInfo>(); }
    //     catch (IOException) { return Array.Empty<FileInfo>(); }
    //     catch { return Array.Empty<FileInfo>(); }
    // }
    //
    // private DirectoryInfo[] SafeGetDirectories(DirectoryInfo dir)
    // {
    //     try { return dir.GetDirectories(); }
    //     catch (UnauthorizedAccessException) { return Array.Empty<DirectoryInfo>(); }
    //     catch (IOException) { return Array.Empty<DirectoryInfo>(); }
    //     catch { return Array.Empty<DirectoryInfo>(); }
    // }

    // ===================== COMANDOS =====================
    private void OpenFile(FileSystemItem? item)
    {
        if (item == null) return;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", item.FullPath);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", item.FullPath);
        }
        catch
        {
        }
    }

    private void OpenFolder(FileSystemItem? item)
    {
        if (item == null) return;
        string? folder = item.IsDirectory ? item.FullPath : Path.GetDirectoryName(item.FullPath);
        if (string.IsNullOrEmpty(folder)) return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", folder);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", folder);
        }
        catch
        {
        }
    }

    private async void CopyPath(FileSystemItem? item)
    {
        if (item == null) return;
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            await desktop.MainWindow.Clipboard.SetTextAsync(item.FullPath);
        }
    }

    private async void DeleteFileFromList(FileSystemItem? item)
    {
        if (item == null) return;
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        bool confirm =
            await _messengerService.ShowConfirmationDialog(desktop.MainWindow!,
                $"¿Deseas enviar {item.Name} a la papelera?");
        if (!confirm) return;

        bool success = false;
        try
        {
            success = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? MoveToTrashWindows(item)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? await MoveToTrashLinuxAsync(item)
                    : await MoveToTrashMacAsync(item);

            if (success) RemoveFromRoot(item);

            await _messengerService.ShowMessageDialog(desktop.MainWindow!,
                success
                    ? $"Fichero {item.Name} enviado a la papelera."
                    : $"No se pudo enviar {item.Name} a la papelera.");
        }
        catch (Exception ex)
        {
            await _messengerService.ShowMessageDialog(desktop.MainWindow!, $"Error: {ex.Message}");
        }
    }

    private static bool MoveToTrashWindows(FileSystemItem item)
    {
        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.FullPath,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> MoveToTrashLinuxAsync(FileSystemItem item)
    {
        var p = Process.Start(new ProcessStartInfo("gio", $"trash \"{item.FullPath}\"") { UseShellExecute = false });
        if (p == null) return false;
        await p.WaitForExitAsync();
        return p.ExitCode == 0;
    }

    private static async Task<bool> MoveToTrashMacAsync(FileSystemItem item)
    {
        string cmd = $"tell application \"Finder\" to delete POSIX file \"{item.FullPath}\"";
        var p = Process.Start(new ProcessStartInfo("osascript", $"-e \"{cmd}\"") { UseShellExecute = true });
        if (p == null) return false;
        await p.WaitForExitAsync();
        return p.ExitCode == 0;
    }

    private void RemoveFromRoot(FileSystemItem item)
    {
        if (RootItems.Contains(item))
        {
            RootItems.Remove(item);
            return;
        }

        foreach (var root in RootItems) RemoveRecursive(root, item);
    }

    private void RemoveRecursive(FileSystemItem parent, FileSystemItem item)
    {
        if (parent.Children.Contains(item)) parent.Children.Remove(item);
        foreach (var child in parent.Children.Where(c => c.IsDirectory)) RemoveRecursive(child, item);
    }

    public void Search(string fileName)
    {
        var win = new SearchResults(fileName);
        win.Show();
    }

    // ===================== Helpers =====================
    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}