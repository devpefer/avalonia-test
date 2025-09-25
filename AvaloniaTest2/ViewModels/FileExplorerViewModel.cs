using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using AvaloniaTest2;
using AvaloniaTest2.Enums;
using AvaloniaTest2.Interfaces;
using AvaloniaTest2.Models;

// ... tus using y namespace permanecen iguales

public class FileExplorerViewModel : INotifyPropertyChanged
{
    private readonly IMessengerService _messengerService;
    private readonly HashSet<string> _visitedPaths = new(StringComparer.OrdinalIgnoreCase);

    private readonly string[] _blockedPaths = OperatingSystem.IsWindows()
        ? new[] { @"C:\Windows\WinSxS", @"C:\Windows\System32\config" }
        : new[] { "/proc", "/sys", "/dev", "/run", "/var/run", "/System", "/Library", "/private" };

    private CancellationToken _cancellation;

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

                await LoadRecursiveAsync(childDir); // carga hijos recursivamente
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

        // Primero los directorios hijos
        foreach (var childDir in dirItem.Children.Where(c => c.IsDirectory))
        {
            total += await CalculateDirectorySizeBottomUpAsync(childDir);
        }

        // Luego los archivos
        foreach (var file in dirItem.Children.Where(c => !c.IsDirectory))
        {
            total += file.LogicalSize;
        }

        // Actualizar tamaño en UI y ordenar hijos
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

    // ===================== COMANDOS =====================
    private void OpenFile(FileSystemItem? item)
    {
        if (item == null || item.IsDirectory) return;
        try { Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true }); }
        catch (Exception ex) { _messengerService.ShowMessageDialog(null, $"No se pudo abrir el archivo:\n{ex.Message}"); }
    }

    private async void OpenFolder(FileSystemItem? item)
    {
        if (item == null) return;
        string path = item.IsDirectory ? item.FullPath : Path.GetDirectoryName(item.FullPath)!;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { await _messengerService.ShowMessageDialog(null, $"No se pudo abrir la carpeta:\n{ex.Message}"); }
    }

    private async void CopyPath(FileSystemItem item)
    {
        if (item == null) return;
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                await desktop.MainWindow.Clipboard.SetTextAsync(item.FullPath);
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
            if (item.IsDirectory) Directory.Delete(item.FullPath, true);
            else File.Delete(item.FullPath);

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