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
    IMessengerService _messengerService;
    private HashSet<string> _visitedPaths = new(StringComparer.OrdinalIgnoreCase);
    public event Action<FileSystemItem>? ItemSelectedRequested;
    private FileSystemItem? _selectedItem;
    public FileSystemItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (_selectedItem != value)
            {
                _selectedItem = value;
                OnPropertyChanged();
            }
        }
    }

    

    private string _searchQuery;

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery != value)
            {
                _searchQuery = value;
                OnPropertyChanged();
            }
        }
    }


    private string? _currentItemBeingProcessed;

    public string? CurrentItemBeingProcessed
    {
        get => _currentItemBeingProcessed;
        set
        {
            if (_currentItemBeingProcessed != value)
            {
                _currentItemBeingProcessed = value;
                OnPropertyChanged();
            }
        }
    }

    private int _pendingSizeTasks = 0;
    private bool _isCalculatingSizes;

    public bool IsCalculatingSizes
    {
        get => _isCalculatingSizes;
        set
        {
            if (_isCalculatingSizes != value)
            {
                _isCalculatingSizes = value;
                OnPropertyChanged();
            }
        }
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
        set
        {
            if (_selectedSort != value)
            {
                _selectedSort = value;
                OnPropertyChanged();
                ApplySortingToAll();
            }
        }
    }
    
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
            AddDriveRoot("/", "/", null);
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
            SortMode.SizeDesc => parent.Children.OrderByDescending(c => c.LogicalSize > 0 ? c.LogicalSize : 0).ToList(),
            _ => parent.Children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList()
        };

        for (int i = 0; i < sorted.Count; i++)
        {
            int currentIndex = parent.Children.IndexOf(sorted[i]);
            if (currentIndex != i)
                parent.Children.Move(currentIndex, i);
        }

        // Recursivamente para subdirectorios
        foreach (var child in parent.Children.Where(c => c.IsDirectory))
            SortChildrenInPlace(child, sortMode);
    }

    private async Task AddDriveRoot(string name, string fullPath, DriveInfo? drive)
    {
        long size = -1;
        try
        {
            if (drive?.IsReady == true) size = drive.TotalSize;
        }
        catch
        {
        }

        var item = new FileSystemItem
        {
            Name = name,
            FullPath = fullPath,
            IsDirectory = true,
            LogicalSize = size,
        };
        item.Children.Add(new FileSystemItem { Name = "Cargando...", LogicalSize = -1 });
        RootItems.Add(item);
    }

    public async Task LoadChildren(FileSystemItem parent)
{
    if (!parent.IsDirectory) return;

    parent.Children.Clear();

    try
    {
        // Lanza cálculo recursivo de todos los subdirectorios y archivos
        await Task.Run(() => StartCalculatingSizesRecursively(parent));
    }
    catch
    {
        // Ignorar errores de acceso
    }
}
    
    private async Task StartCalculatingSizesRecursively(FileSystemItem parent)
{
    if (!parent.IsDirectory) return;
    if (!_visitedPaths.Add(parent.FullPath)) return;

    string[] files = Array.Empty<string>();
    string[] directories = Array.Empty<string>();

    // Obtener archivos, ignorando accesos denegados
    try { files = Directory.GetFiles(parent.FullPath); } catch { }
    try { directories = Directory.GetDirectories(parent.FullPath); } catch { }

    // Archivos
    foreach (var file in files)
    {
        try
        {
            var fi = new FileInfo(file);
            if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) continue;

            var childFile = new FileSystemItem
            {
                Name = fi.Name,
                FullPath = fi.FullName,
                IsDirectory = false,
                LogicalSize = fi.Length,
                Parent = parent
            };

            Dispatcher.UIThread.Post(() => {
                parent.Children.Add(childFile);
                parent.LogicalSize += childFile.LogicalSize;
            });
        }
        catch { /* ignorar errores de lectura individual */ }
    }

    // Directorios
    foreach (var dir in directories)
    {
        try
        {
            var di = new DirectoryInfo(dir);
            if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;

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
                long size = await GetDirectorySizeSafeAsync(di);
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

            // Recurse seguro
            StartCalculatingSizesRecursively(childDir);
        }
        catch { /* ignorar accesos denegados */ }
    }
}

    
    private async Task StartCalculatingSizesRecursivelyOLDNEW(FileSystemItem parent)
{
    if (!parent.IsDirectory) return;

    // Evitar procesar el mismo path dos veces (previene bucles)
    if (!_visitedPaths.Add(parent.FullPath)) return;

    // Archivos del directorio actual
    foreach (var file in Directory.GetFiles(parent.FullPath))
    {
        try
        {
            var fi = new FileInfo(file);
            if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) continue;

            var childFile = new FileSystemItem
            {
                Name = fi.Name,
                FullPath = fi.FullName,
                IsDirectory = false,
                LogicalSize = fi.Length,
                Parent = parent
            };

            Dispatcher.UIThread.Post(() => parent.Children.Add(childFile));
            Dispatcher.UIThread.Post(() => parent.LogicalSize += childFile.LogicalSize);
        }
        catch { }
    }

    // Subdirectorios
    foreach (var dir in Directory.GetDirectories(parent.FullPath))
    {
        try
        {
            var di = new DirectoryInfo(dir);
            if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue; // ignorar enlaces simbólicos

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
                long size = await GetDirectorySizeSafeAsync(di);
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

            // Recurse automáticamente aunque el nodo no esté expandido
            StartCalculatingSizesRecursively(childDir);
        }
        catch { }
    }
}

// Recorre todos los subdirectorios aunque no estén expandidos
private async Task StartCalculatingSizesRecursivelyOLD(FileSystemItem parent)
{
    if (!parent.IsDirectory) return;

    // Archivos del directorio actual
    foreach (var file in Directory.GetFiles(parent.FullPath))
    {
        try
        {
            var fi = new FileInfo(file);
            var childFile = new FileSystemItem
            {
                Name = fi.Name,
                FullPath = fi.FullName,
                IsDirectory = false,
                LogicalSize = fi.Length,
                Parent = parent
            };

            Dispatcher.UIThread.Post(() => parent.Children.Add(childFile));

            // Actualizar tamaño del padre en la UI
            Dispatcher.UIThread.Post(() => parent.LogicalSize += childFile.LogicalSize);
        }
        catch { }
    }

    // Subdirectorios
    foreach (var dir in Directory.GetDirectories(parent.FullPath))
    {
        try
        {
            var di = new DirectoryInfo(dir);
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

            // Calcular tamaño del subdirectorio de forma asíncrona
            _ = Task.Run(async () =>
            {
                long size = await GetDirectorySizeSafeAsync(di);
                childDir.LogicalSize = size;

                // Actualizar tamaño acumulado recursivamente hacia los padres
                await UpdateParentSizesAsync(childDir);

                if (Interlocked.Decrement(ref _pendingSizeTasks) == 0)
                {
                    ApplySortingToAll();
                    IsCalculatingSizes = false;
                    CurrentItemBeingProcessed = null;
                    SizesCalculationCompleted?.Invoke();
                }
            });

            // Recurse automáticamente aunque el nodo no esté expandido
            StartCalculatingSizesRecursively(childDir);
        }
        catch { }
    }
}

// Propaga el tamaño de un hijo hacia arriba hasta la raíz
private async Task UpdateParentSizesAsync(FileSystemItem item)
{
    var parent = item.Parent;
    if (parent == null) return;

    await Dispatcher.UIThread.InvokeAsync(() =>
    {
        parent.LogicalSize = parent.Children.Sum(c => c.LogicalSize > 0 ? c.LogicalSize : 0);
    });

    // Recursivamente hacia arriba
    if (parent.Parent != null)
        await UpdateParentSizesAsync(parent);
}


    private async Task<long> GetDirectorySizeSafeAsync(DirectoryInfo dir, Action<string>? onProgress = null,
        int timeoutMsPerDir = 2000)
    {
        return await Task.Run(async () =>
        {
            long size = 0;

            try
            {
                // Archivos
                foreach (var f in dir.GetFiles())
                {
                    try
                    {
                        if ((f.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        long fileSize = GetFileSizeWithTimeout(f.FullName, timeoutMsPerDir);
                        size += fileSize;
                        CurrentItemBeingProcessed = f.FullName;
                    }
                    catch { }
                }

                // Subdirectorios
                foreach (var sub in dir.GetDirectories())
                {
                    try
                    {
                        if ((sub.Attributes & FileAttributes.ReparsePoint) != 0 ||
                            (sub.Attributes & FileAttributes.Device) != 0)
                            continue; // saltar enlaces y dispositivos

                        onProgress?.Invoke(sub.FullName);

                        var task = GetDirectorySizeSafeAsync(sub, onProgress, timeoutMsPerDir);
                        if (await Task.WhenAny(task, Task.Delay(timeoutMsPerDir)) == task)
                            size += task.Result; // si terminó antes del timeout
                        // si timeout, se ignora
                    }
                    catch
                    {
                        // ignorar errores de acceso
                    }
                }
            }
            catch
            {
                // ignorar acceso denegado u otros errores
            }

            return size;
        });
    }


    private long GetDirectorySizeSafe(DirectoryInfo dir)
    {
        long size = 0;
        try
        {
            foreach (var f in dir.GetFiles()) size += f.Length;
            foreach (var sub in dir.GetDirectories()) size += GetDirectorySizeSafe(sub);
        }
        catch
        {
        }

        return size;
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

        bool confirm =
            await _messengerService.ShowConfirmationDialog(mainWindow, $"¿Deseas enviar el fichero a la papelera?\n{item.Name}");
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
            // Windows: P/Invoke a SHFileOperation
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
            // Linux: usa gio trash si existe
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
            // macOS: usa AppleScript
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
        // Primero revisa la raíz
        if (RootItems.Contains(item))
        {
            RootItems.Remove(item);
            return;
        }

        // Recorre recursivamente todos los hijos
        foreach (var root in RootItems)
        {
            if (root.IsDirectory)
                RemoveItemRecursive(root, item);
        }
    }

    private void GetDriveTotalSize()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                Drives.Add(drive);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"No se pudo acceder a {drive.Name}: {ex.Message}");
            }
        }
    }

    private long GetFileSizeWithTimeout(string path, int timeoutMs = 500)
    {
        // Ignorar ficheros de sistema problemáticos
        var attr = File.GetAttributes(path);
        if ((attr & FileAttributes.System) != 0) return 0;

        long size = 0;
        var thread = new Thread(() =>
        {
            try { size = new FileInfo(path).Length; }
            catch { size = 0; }
        });
        thread.IsBackground = true;
        thread.Start();

        if (!thread.Join(timeoutMs))
        {
            thread.Abort(); // solo en último recurso; asegura que no bloquee más
            size = 0;
        }

        return size;
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