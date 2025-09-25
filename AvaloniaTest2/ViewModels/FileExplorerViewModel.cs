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
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using AvaloniaTest2;
using AvaloniaTest2.Enums;
using AvaloniaTest2.Interfaces;
using AvaloniaTest2.Models;

namespace AvaloniaTest2.ViewModels;
public class FileExplorerViewModel : INotifyPropertyChanged
{
    private readonly IMessengerService _messengerService;

    private readonly string[] _blockedPaths = OperatingSystem.IsWindows()
        ? new[] { @"C:\Windows\WinSxS", @"C:\Windows\System32\config" }
        : new[] { "/proc", "/sys", "/dev", "/run", "/var/run", "/System", "/Library", "/private" };
    
    public ObservableCollection<FileSystemItem> RootItems { get; } = new();
    public Array SortModes => Enum.GetValues(typeof(SortMode));
    private FileSystemItem? _selectedItem;
    public FileSystemItem? SelectedItem { get => _selectedItem; set => SetProperty(ref _selectedItem, value); }

    private bool _isCalculatingSizes;
    public bool IsCalculatingSizes { get => _isCalculatingSizes; private set => SetProperty(ref _isCalculatingSizes, value); }

    private string? _currentItemBeingProcessed;
    public string? CurrentItemBeingProcessed { get => _currentItemBeingProcessed; private set => SetProperty(ref _currentItemBeingProcessed, value); }

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
    public event Action? SizesCalculationCompleted;
    public event PropertyChangedEventHandler? PropertyChanged;

    public FileExplorerViewModel(IMessengerService messengerService)
    {
        _messengerService = messengerService;
        OpenFileCommand = new RelayCommand<FileSystemItem>(OpenFile);
        OpenFolderCommand = new RelayCommand<FileSystemItem>(OpenFolder);
        CopyPathCommand = new RelayCommand<FileSystemItem>(CopyPath);
        MoveToTrashCommand = new RelayCommand<FileSystemItem>(DeleteFileFromList);

        LoadAll();
    }

    private void LoadAll()
    {
        string rootPath = OperatingSystem.IsWindows() ? "C:\\" : "/";
        var rootItem = new FileSystemItem { Name = rootPath, FullPath = rootPath, IsDirectory = true };
        RootItems.Add(rootItem);
        _ = Task.Run(async () =>
        {
            await LoadRecursiveAsync(rootItem);
            await CalculateDirectorySizeBottomUpAsync(rootItem);
            await Dispatcher.UIThread.InvokeAsync(() => SizesCalculationCompleted?.Invoke());
        });
    }

    private async Task LoadRecursiveAsync(FileSystemItem parent)
    {
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(parent.FullPath); }
        catch { return; }

        foreach (var entry in entries)
        {
            bool isDir = false;
            try { isDir = Directory.Exists(entry); } catch { continue; }

            if (isDir)
            {
                if (_blockedPaths.Any(bp => entry.StartsWith(bp, StringComparison.OrdinalIgnoreCase))) continue;

                var childDir = new FileSystemItem { Name = Path.GetFileName(entry), FullPath = entry, IsDirectory = true, Parent = parent };
                await Dispatcher.UIThread.InvokeAsync(() => parent.Children.Add(childDir));

                await LoadRecursiveAsync(childDir);
            }
            else
            {
                FileInfo fi;
                try { fi = new FileInfo(entry); } catch { continue; }

                if ((fi.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device | FileAttributes.System)) != 0) continue;

                var fileItem = new FileSystemItem
                {
                    Name = fi.Name,
                    FullPath = fi.FullName,
                    IsDirectory = false,
                    LogicalSize = fi.Length,
                    Parent = parent
                };
                await Dispatcher.UIThread.InvokeAsync(() => parent.Children.Add(fileItem));
            }
        }
    }

    /// <summary>
    /// Calcula el tamaño de un directorio de forma bottom-up: espera a que todos los hijos calculen antes de sumar.
    /// </summary>
    private async Task<long> CalculateDirectorySizeBottomUpAsync(FileSystemItem dirItem)
    {
        long total = 0;

        foreach (var childDir in dirItem.Children.Where(c => c.IsDirectory))
        {
            total += await CalculateDirectorySizeBottomUpAsync(childDir);
        }

        foreach (var file in dirItem.Children.Where(c => !c.IsDirectory))
        {
            total += file.LogicalSize;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            dirItem.LogicalSize = total;
            SortRecursive(dirItem, SelectedSort);
        });

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
    
    private void OpenFile(FileSystemItem? item)
    {
        if (item == null) return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start(new ProcessStartInfo("xdg-open", item.FullPath) { UseShellExecute = false });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo("open", item.FullPath) { UseShellExecute = false });
            }
        }
        catch
        {
        }
    }

    private void OpenFolder(FileSystemItem? item)
    {
        if (item == null) return;

        try
        {
            string? folderPath = Path.GetDirectoryName(item.FullPath);
            if (string.IsNullOrEmpty(folderPath)) return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", folderPath) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start(new ProcessStartInfo("xdg-open", folderPath) { UseShellExecute = false });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo("open", folderPath) { UseShellExecute = false });
            }
        }
        catch
        {
        }
    }

    private async void CopyPath(FileSystemItem? item)
    {
        if (item == null) return;

        await Dispatcher.UIThread.InvokeAsync(async () => { await CopiarAlPortapapeles(item.FullPath); });
    }
    
    private async Task CopiarAlPortapapeles(string texto)
    {
        var window =
            Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

        if (window != null)
        {
            var clipboard = window.Clipboard;
            await clipboard?.SetTextAsync(texto)!;
        }
    }

    private async void DeleteFileFromList(FileSystemItem? item)
    {
        if (item == null) return;

        var mainWindow = Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (mainWindow == null) return;

        bool confirm = await _messengerService.ShowConfirmationDialog(mainWindow, $"¿Deseas enviar el fichero a la papelera?\n{item.Name}");
        if (!confirm) return;

        try
        {
            bool ok = MoveToTrash(item);
            if (ok)
            {
                DeleteItemFromRootItems(item);

                await _messengerService.ShowMessageDialog(mainWindow, $"Fichero {item.Name} enviado a la papelera correctamente.");
            }
            else
            {
                await _messengerService.ShowMessageDialog(mainWindow, $"No se pudo enviar el fichero {item.Name} a la papelera.");
            }
        }
        catch (Exception ex)
        {
            await _messengerService.ShowMessageDialog(mainWindow, $"Error al mover a la papelera:\n{ex.Message}");
        }
    }
    
    private bool MoveToTrash(FileSystemItem item)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
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
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var psi = new ProcessStartInfo("gio", $"trash \"{item.FullPath}\"")
            {
                UseShellExecute = false
            };
            var p = Process.Start(psi);
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string cmd = $"tell application \"Finder\" to delete POSIX file \"{item.FullPath}\"";
            var psi = new ProcessStartInfo("osascript", $"-e \"{cmd}\"")
            {
                UseShellExecute = true
            };
            var p = Process.Start(psi);
            p.WaitForExit();
            return p.ExitCode == 0;
        }

        return false;
    }
    
    private void RemoveItemRecursive(FileSystemItem parent, FileSystemItem item)
    {
        if (parent.Children.Contains(item))
        {
            parent.Children.Remove(item);
            return;
        }

        foreach (var child in parent.Children)
        {
            if (child.IsDirectory)
                RemoveItemRecursive(child, item);
        }
    }
    
    private void DeleteItemFromRootItems(FileSystemItem item)
    {
        if (RootItems.Contains(item))
        {
            RootItems.Remove(item);
            return;
        }

        foreach (var root in RootItems)
        {
            if (root.IsDirectory)
                RemoveItemRecursive(root, item);
        }
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}