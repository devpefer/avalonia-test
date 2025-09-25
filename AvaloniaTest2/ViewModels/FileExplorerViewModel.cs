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
using DriveInfo = System.IO.DriveInfo;

namespace AvaloniaTest2.ViewModels;

public class FileExplorerViewModel : INotifyPropertyChanged
{
    private readonly IMessengerService _messengerService;
    private readonly HashSet<string> _visitedPaths = new(StringComparer.OrdinalIgnoreCase);

    public event Action<FileSystemItem>? ItemSelectedRequested;

    private FileSystemItem? _selectedItem;
    public FileSystemItem? SelectedItem
    {
        get => _selectedItem;
        set { _selectedItem = value; OnPropertyChanged(); }
    }

    private string _searchQuery = "";
    public string SearchQuery
    {
        get => _searchQuery;
        set { _searchQuery = value; OnPropertyChanged(); }
    }

    private string? _currentItemBeingProcessed;
    public string? CurrentItemBeingProcessed
    {
        get => _currentItemBeingProcessed;
        set { _currentItemBeingProcessed = value; OnPropertyChanged(); }
    }

    private int _pendingSizeTasks = 0;
    private bool _isCalculatingSizes;
    public bool IsCalculatingSizes
    {
        get => _isCalculatingSizes;
        set { _isCalculatingSizes = value; OnPropertyChanged(); }
    }

    public event Action? SizesCalculationCompleted;

    public ICommand OpenFileCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand CopyPathCommand { get; }
    public ICommand MoveToTrashCommand { get; }
    public ICommand SearchCommand { get; }

    public ObservableCollection<FileSystemItem> RootItems { get; } = new();
    public ObservableCollection<DriveInfo> Drives { get; } = new();
    public Array SortModes => Enum.GetValues(typeof(SortMode));

    private SortMode _selectedSort = SortMode.SizeDesc;
    public SortMode SelectedSort
    {
        get => _selectedSort;
        set { _selectedSort = value; OnPropertyChanged(); ApplySortingToAll(); }
    }

    private readonly string[] blockedPaths = new[]
    {
        @"C:\Windows\WinSxS",
        @"C:\Windows\System32\config"
    };

    public FileExplorerViewModel(IMessengerService messengerService)
    {
        _messengerService = messengerService;
        GetDriveTotalSize();

        OpenFileCommand = new RelayCommand<FileSystemItem>(OpenFile);
        OpenFolderCommand = new RelayCommand<FileSystemItem>(OpenFolder);
        CopyPathCommand = new RelayCommand<FileSystemItem>(CopyPath);
        MoveToTrashCommand = new RelayCommand<FileSystemItem>(DeleteFileFromList);
        SearchCommand = new RelayCommand<string>(Search);

        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives())
                AddDriveRoot(drive.Name, drive.RootDirectory.FullName, drive);
        }
        else
        {
            AddDriveRoot("/", "/", null);
        }
    }

    private void ApplySortingToAll()
    {
        foreach (var root in RootItems)
            SortChildrenInPlace(root, SelectedSort);
    }

    private void SortChildrenInPlace(FileSystemItem parent, SortMode sortMode)
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
            SortChildrenInPlace(child, sortMode);
    }

    private async Task AddDriveRoot(string name, string fullPath, DriveInfo? drive)
    {
        long size = -1;
        try { if (drive?.IsReady == true) size = drive.TotalSize; } catch { }

        var item = new FileSystemItem
        {
            Name = name,
            FullPath = fullPath,
            IsDirectory = true,
            LogicalSize = size
        };

        item.Children.Add(new FileSystemItem { Name = "Cargando...", LogicalSize = -1 });
        RootItems.Add(item);
    }

    public async Task LoadChildren(FileSystemItem parent)
    {
        if (!parent.IsDirectory) return;

        parent.Children.Clear();
        await Task.Run(() => StartCalculatingSizesRecursively(parent));
    }

    private async Task StartCalculatingSizesRecursively(FileSystemItem parent)
    {
        if (!parent.IsDirectory || !_visitedPaths.Add(parent.FullPath)) return;

        string[] files = Array.Empty<string>();
        string[] directories = Array.Empty<string>();

        try { files = Directory.GetFiles(parent.FullPath); } catch { }
        try { directories = Directory.GetDirectories(parent.FullPath); } catch { }

        foreach (var file in files)
        {
            try
            {
                if (blockedPaths.Any(bp => file.StartsWith(bp, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var fi = new FileInfo(file);
                if ((fi.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device | FileAttributes.System)) != 0)
                    continue;

                var childFile = new FileSystemItem
                {
                    Name = fi.Name,
                    FullPath = fi.FullName,
                    IsDirectory = false,
                    LogicalSize = fi.Length,
                    Parent = parent
                };

                Dispatcher.UIThread.Post(() =>
                {
                    parent.Children.Add(childFile);
                    parent.LogicalSize += childFile.LogicalSize;
                });
            }
            catch { }
        }

        foreach (var dir in directories)
        {
            try
            {
                if (blockedPaths.Any(bp => dir.StartsWith(bp, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var di = new DirectoryInfo(dir);
                if ((di.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    continue;

                var childDir = new FileSystemItem
                {
                    Name = di.Name,
                    FullPath = di.FullName,
                    IsDirectory = true,
                    LogicalSize = 0,
                    Parent = parent
                };

                Dispatcher.UIThread.Post(() => parent.Children.Add(childDir));

                Interlocked.Increment(ref _pendingSizeTasks);
                IsCalculatingSizes = true;

                _ = Task.Run(async () =>
                {
                    long size = await GetDirectorySizeSafeAsync(di, 2000, 8);
                    childDir.LogicalSize = size;
                    await UpdateParentSizesAsync(childDir);

                    if (Interlocked.Decrement(ref _pendingSizeTasks) == 0)
                    {
                        ApplySortingToAll();
                        IsCalculatingSizes = false;
                        CurrentItemBeingProcessed = null;
                        SizesCalculationCompleted?.Invoke();
                    }
                });

                StartCalculatingSizesRecursively(childDir);
            }
            catch { }
        }
    }

    private async Task UpdateParentSizesAsync(FileSystemItem item)
    {
        var parent = item.Parent;
        if (parent == null) return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            long newSize = parent.Children.Sum(c => c.LogicalSize > 0 ? c.LogicalSize : 0);
            parent.LogicalSize = newSize;
        });

        if (parent.Parent != null)
            await UpdateParentSizesAsync(parent);
    }

    private async Task<long> GetDirectorySizeSafeAsync(DirectoryInfo dir, int timeoutMsPerFile = 500, int maxConcurrentTasks = 8)
    {
        long totalSize = 0;
        var files = Array.Empty<FileInfo>();
        var subDirs = Array.Empty<DirectoryInfo>();

        try { files = dir.GetFiles(); } catch { }
        try { subDirs = dir.GetDirectories(); } catch { }

        object sizeLock = new();
        using var semaphore = new SemaphoreSlim(maxConcurrentTasks);

        var fileTasks = files.Select(async f =>
        {
            await semaphore.WaitAsync();
            try
            {
                if ((f.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device | FileAttributes.System)) != 0)
                    return;

                if (blockedPaths.Any(bp => f.FullName.StartsWith(bp, StringComparison.OrdinalIgnoreCase)))
                    return;

                CurrentItemBeingProcessed = f.FullName;
                var t = Task.Run(() => f.Length);

                if (await Task.WhenAny(t, Task.Delay(timeoutMsPerFile)) == t)
                    lock (sizeLock) totalSize += t.Result;
            }
            catch { }
            finally { semaphore.Release(); }
        }).ToArray();

        var dirTasks = subDirs.Select(async sub =>
        {
            await semaphore.WaitAsync();
            try
            {
                if ((sub.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0) return;
                if (blockedPaths.Any(bp => sub.FullName.StartsWith(bp, StringComparison.OrdinalIgnoreCase))) return;

                CurrentItemBeingProcessed = sub.FullName;
                long subSize = await GetDirectorySizeSafeAsync(sub, timeoutMsPerFile, maxConcurrentTasks);
                lock (sizeLock) totalSize += subSize;
            }
            catch { }
            finally { semaphore.Release(); }
        }).ToArray();

        await Task.WhenAll(fileTasks.Concat(dirTasks));
        return totalSize;
    }

    // --- Métodos UI / Files ---
    private void OpenFile(FileSystemItem? item)
    {
        if (item == null) return;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start(new ProcessStartInfo("xdg-open", item.FullPath) { UseShellExecute = false });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start(new ProcessStartInfo("open", item.FullPath) { UseShellExecute = false });
        }
        catch { }
    }

    private void OpenFolder(FileSystemItem? item)
    {
        if (item == null) return;
        try
        {
            string? folderPath = Path.GetDirectoryName(item.FullPath);
            if (string.IsNullOrEmpty(folderPath)) return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo("explorer.exe", folderPath) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start(new ProcessStartInfo("xdg-open", folderPath) { UseShellExecute = false });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start(new ProcessStartInfo("open", folderPath) { UseShellExecute = false });
        }
        catch { }
    }

    private async void CopyPath(FileSystemItem? item)
    {
        if (item == null) return;

        var window = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (window != null)
            await window.Clipboard.SetTextAsync(item.FullPath);
    }

    private async void DeleteFileFromList(FileSystemItem? item)
    {
        if (item == null) return;

        var mainWindow = Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (mainWindow == null) return;

        bool confirm = await _messengerService.ShowConfirmationDialog(mainWindow,
            $"¿Deseas enviar el fichero a la papelera?\n{item.Name}");
        if (!confirm) return;

        try
        {
            bool ok = MoveToTrash(item);
            if (ok) DeleteItemFromRootItems(item);
            await _messengerService.ShowMessageDialog(mainWindow,
                ok ? $"Fichero {item.Name} enviado a la papelera correctamente."
                    : $"No se pudo enviar el fichero {item.Name} a la papelera.");
        }
        catch (Exception ex)
        {
            await _messengerService.ShowMessageDialog(mainWindow, $"Error al mover a la papelera:\n{ex.Message}");
        }
    }

    private bool MoveToTrash(FileSystemItem item)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.FullPath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                return true;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var psi = new ProcessStartInfo("gio", $"trash \"{item.FullPath}\"") { UseShellExecute = false };
                var p = Process.Start(psi);
                p.WaitForExit();
                return p.ExitCode == 0;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string cmd = $"tell application \"Finder\" to delete POSIX file \"{item.FullPath}\"";
                var psi = new ProcessStartInfo("osascript", $"-e \"{cmd}\"") { UseShellExecute = true };
                var p = Process.Start(psi);
                p.WaitForExit();
                return p.ExitCode == 0;
            }
        }
        catch { }

        return false;
    }

    private void DeleteItemFromRootItems(FileSystemItem item)
    {
        if (RootItems.Contains(item)) { RootItems.Remove(item); return; }
        foreach (var root in RootItems) RemoveItemRecursive(root, item);
    }

    private void RemoveItemRecursive(FileSystemItem parent, FileSystemItem item)
    {
        if (parent.Children.Contains(item)) parent.Children.Remove(item);
        foreach (var child in parent.Children.Where(c => c.IsDirectory))
            RemoveItemRecursive(child, item);
    }

    private void GetDriveTotalSize()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try { if (drive.IsReady) Drives.Add(drive); } catch { }
        }
    }

    private async void Search(string fileName)
    {
        var window = new SearchResults(fileName);
        window.Show();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
