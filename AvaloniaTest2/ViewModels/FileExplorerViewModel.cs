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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaTest2.Enums;
using AvaloniaTest2.Helpers;
using AvaloniaTest2.Models;
using FluentAvalonia.UI.Data;
using Microsoft.VisualBasic;
using DriveInfo = System.IO.DriveInfo;

namespace AvaloniaTest2.ViewModels;

public class FileExplorerViewModel : INotifyPropertyChanged
{
    private CancellationTokenSource? _searchCancellationTokenSource;

    public CancellationTokenSource? SearchCancellationTokenSource
    {
        get => _searchCancellationTokenSource;
        set
        {
            if (_searchCancellationTokenSource != value)
            {
                _searchCancellationTokenSource = value;
                OnPropertyChanged();
                (CancelSearchCommand as RelayCommand<FileSystemItem>)?.RaiseCanExecuteChanged();
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

    private readonly List<FileSystemItem> _searchResults = new();
    private int _currentResultIndex = -1;

    public string SearchStatus =>
        _searchResults.Count == 0
            ? "No hay resultados"
            : $"{_currentResultIndex + 1} de {_searchResults.Count}";

    public ICollectionView RootItemsView { get; private set; }
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
    public ICommand NextResultCommand { get; }
    public ICommand PrevResultCommand { get; }
    public ICommand CancelSearchCommand { get; }
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

    public FileExplorerViewModel()
    {
        GetDriveTotalSize();
        OpenFileCommand = new RelayCommand<FileSystemItem>(OpenFile);
        OpenFolderCommand = new RelayCommand<FileSystemItem>(OpenFolder);
        CopyPathCommand = new RelayCommand<FileSystemItem>(CopyPath);
        MoveToTrashCommand = new RelayCommand<FileSystemItem>(DeleteFileFromList);
        SearchCommand = new RelayCommand<FileSystemItem>(_ => PerformSearch());
        NextResultCommand = new RelayCommand<FileSystemItem>(_ => NavigateResult(1), _ => _searchResults.Count > 1);
        PrevResultCommand = new RelayCommand<FileSystemItem>(_ => NavigateResult(-1), _ => _searchResults.Count > 1);
        CancelSearchCommand =
            new RelayCommand<FileSystemItem>(_ => CancelSearch(), _ => CancelSearchCanExecute());
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
            SortMode.SizeDesc => parent.Children.OrderByDescending(c => c.Size > 0 ? c.Size : 0).ToList(),
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
            Size = size
        };
        item.Children.Add(new FileSystemItem { Name = "Cargando...", Size = -1 });
        RootItems.Add(item);
    }

    public async void LoadChildren(FileSystemItem parent)
    {
        if (!parent.IsDirectory) return;
        parent.Children.Clear();

        try
        {
            var dirs = Directory.GetDirectories(parent.FullPath)
                .Select(d => new DirectoryInfo(d))
                .Select(di => new FileSystemItem
                {
                    Name = di.Name,
                    FullPath = di.FullName,
                    IsDirectory = true,
                    Size = -1
                }).ToList();

            var files = Directory.GetFiles(parent.FullPath)
                .Select(f => new FileInfo(f))
                .Select(fi => new FileSystemItem
                {
                    Name = fi.Name,
                    FullPath = fi.FullName,
                    IsDirectory = false,
                    Size = fi.Length
                }).ToList();

            var children = dirs.Concat(files).ToList();
            children = SelectedSort switch
            {
                SortMode.SizeDesc => children.OrderByDescending(c => c.Size > 0 ? c.Size : 0).ToList(),
                _ => children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList()
            };

            foreach (var child in children)
            {
                if (child.IsDirectory)
                    child.Children.Add(new FileSystemItem { Name = "Cargando...", Size = -1 });

                parent.Children.Add(child);

                if (child.IsDirectory)
                {
                    Interlocked.Increment(ref _pendingSizeTasks);
                    IsCalculatingSizes = true;

                    _ = Task.Run(async () =>
                    {
                        //CurrentItemBeingProcessed = child.FullPath; // <--- Aquí actualizas la UI
                        long size = await GetDirectorySizeSafeAsync(new DirectoryInfo(child.FullPath));
                        child.Size = size;

                        await Dispatcher.UIThread.InvokeAsync(() =>
                            parent.Size = parent.Children.Sum(c => c.Size > 0 ? c.Size : 0)
                        );

                        if (Interlocked.Decrement(ref _pendingSizeTasks) == 0)
                        {
                            ApplySortingToAll();
                            IsCalculatingSizes = false;
                            CurrentItemBeingProcessed = null;
                            SizesCalculationCompleted?.Invoke();
                        }
                    });
                }
            }

            parent.Size = parent.Children.Sum(c => c.Size > 0 ? c.Size : 0);
        }
        catch
        {
        }
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
                        if ((f.Attributes & FileAttributes.ReparsePoint) != 0 ||
                            (f.Attributes & FileAttributes.Device) != 0)
                            continue; // saltar enlaces y dispositivos

                        onProgress?.Invoke(f.FullName);
                        CurrentItemBeingProcessed = f.FullName;
                        // lectura de longitud con timeout
                        var task = Task.Run(() => f.Length);
                        if (await Task.WhenAny(task, Task.Delay(timeoutMsPerDir)) == task)
                            size += task.Result; // si terminó antes del timeout
                        // si timeout, se ignora
                    }
                    catch
                    {
                        // ignorar errores de acceso
                    }
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
            await ShowConfirmationDialog(mainWindow, $"¿Deseas enviar el fichero a la papelera?\n{item.Name}");
        if (!confirm) return;

        try
        {
            bool ok = MoveToTrash(item);
            if (ok)
            {
                DeleteItemFromRootItems(item);

                await ShowMessageDialog(mainWindow, $"Fichero {item.Name} enviado a la papelera correctamente.");
            }
            else
            {
                await ShowMessageDialog(mainWindow, $"No se pudo enviar el fichero {item.Name} a la papelera.");
            }
        }
        catch (Exception ex)
        {
            await ShowMessageDialog(mainWindow, $"Error al mover a la papelera:\n{ex.Message}");
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


    private async Task<bool> ShowConfirmationDialog(Window parent, string message)
    {
        var tcs = new TaskCompletionSource<bool>();

        var yesButton = new Button { Content = "Sí", Width = 80 };
        var noButton = new Button { Content = "No", Width = 80, Margin = new Thickness(10, 0, 0, 0) };

        var dialog = new Window
        {
            Title = "Confirmar eliminación",
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(10),
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Margin = new Thickness(0, 10, 0, 0),
                        Children =
                        {
                            yesButton,
                            noButton
                        }
                    }
                }
            }
        };

        yesButton.Click += (_, _) =>
        {
            tcs.SetResult(true);
            dialog.Close();
        };
        noButton.Click += (_, _) =>
        {
            tcs.SetResult(false);
            dialog.Close();
        };

        await dialog.ShowDialog(parent);
        return await tcs.Task;
    }


    private async Task<string?> ShowPasswordDialog(Window parent, string message)
    {
        var tcs = new TaskCompletionSource<string?>();

        string? password = null;
        var passwordBox = new TextBox { Watermark = "Contraseña", PasswordChar = '●' };
        var okButton = new Button { Content = "Aceptar", Width = 80 };
        var cancelButton = new Button { Content = "Cancelar", Width = 80, Margin = new Thickness(10, 0, 0, 0) };

        var dialog = new Window
        {
            Title = "Contraseña requerida",
            Width = 400,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(10),
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    passwordBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 10, 0, 0),
                        Children = { okButton, cancelButton }
                    }
                }
            }
        };

        okButton.Click += (_, _) =>
        {
            password = passwordBox.Text;
            tcs.SetResult(password);
            dialog.Close();
        };
        cancelButton.Click += (_, _) =>
        {
            tcs.SetResult(null);
            dialog.Close();
        };

        await dialog.ShowDialog(parent);
        return await tcs.Task;
    }

    private async Task ShowMessageDialog(Window parent, string message)
    {
        var tcs = new TaskCompletionSource<bool>();

        var closeButton = new Button
        {
            Content = "Cerrar", HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var dialog = new Window
        {
            Title = "Información",
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(10),
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    closeButton
                }
            }
        };

        closeButton.Click += (_, _) =>
        {
            tcs.SetResult(true);
            dialog.Close();
        };

        await dialog.ShowDialog(parent);
        await tcs.Task;
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

    private string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    private void PerformSearch()
    {
        // Cancelar búsqueda previa si existe
        SearchCancellationTokenSource?.Cancel();
        SearchCancellationTokenSource = new CancellationTokenSource();
        var token = SearchCancellationTokenSource.Token;

        _searchResults.Clear();
        _currentResultIndex = -1;

        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            OnPropertyChanged(nameof(SearchStatus));
            return;
        }

        foreach (var root in RootItems)
        {
            _ = Task.Run(() => SearchRecursiveAsync(root.FullPath, SearchQuery, root, token));
        }
    }


    private async Task SearchRecursiveAsync(string path, string query, FileSystemItem parentItem,
        CancellationToken token)
    {
        try
        {
            foreach (var file in Directory.GetFiles(path))
            {
                if (token.IsCancellationRequested) return; // <--- cancelar

                if (Path.GetFileName(file).Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        var item = await FindOrCreateItemForSearchAsync(file, parentItem);
                        _searchResults.Add(item);

                        if (_currentResultIndex == -1)
                        {
                            _currentResultIndex = 0;
                            SelectItem(item);
                        }

                        OnPropertyChanged(nameof(SearchStatus));
                    });
                }
            }

            foreach (var dir in Directory.GetDirectories(path))
            {
                if (token.IsCancellationRequested) return; // <--- cancelar

                FileSystemItem dirItem = null;
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    dirItem = await FindOrCreateItemForSearchAsync(dir, parentItem);

                    if (Path.GetFileName(dir).Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        _searchResults.Add(dirItem);

                        if (_currentResultIndex == -1)
                        {
                            _currentResultIndex = 0;
                            SelectItem(dirItem);
                        }

                        OnPropertyChanged(nameof(SearchStatus));
                    }
                });

                _ = SearchRecursiveAsync(dir, query, dirItem!, token);
            }
        }
        catch
        {
            // ignorar errores de acceso
        }
    }

    private async Task<FileSystemItem> FindOrCreateItemForSearchAsync(string fullPath, FileSystemItem root)
    {
        // Solo buscamos los nodos existentes, no agregamos hijos
        FileSystemItem? current = root;

        var segments = fullPath.Substring(root.FullPath.Length).TrimStart(Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(s => !string.IsNullOrEmpty(s));

        foreach (var segment in segments)
        {
            var child = current.Children.FirstOrDefault(c => c.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (child != null)
            {
                current = child;
            }
            else
            {
                // Si no existe, creamos un nodo temporal (solo para selección) sin tocar el árbol real
                current = new FileSystemItem
                {
                    Name = segment,
                    FullPath = Path.Combine(current.FullPath, segment),
                    IsDirectory = true
                };
            }
        }

        return current;
    }


    private async Task<FileSystemItem> FindOrCreateItemAsync(string fullPath, FileSystemItem parent, bool isDirectory)
    {
        // Buscar hijo existente
        var existing = parent.Children.FirstOrDefault(c =>
            c.FullPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return existing;

        // Si no existe, crearlo
        var item = new FileSystemItem
        {
            Name = Path.GetFileName(fullPath),
            FullPath = fullPath,
            IsDirectory = isDirectory,
            Parent = parent
        };

        if (isDirectory)
            item.Children.Add(new FileSystemItem { Name = "Cargando...", Size = -1 });

        parent.Children.Add(item);
        return item;
    }


    private void NavigateResult(int direction)
    {
        if (_searchResults.Count == 0) return;

        _currentResultIndex = (_currentResultIndex + direction + _searchResults.Count) % _searchResults.Count;
        SelectItem(_searchResults[_currentResultIndex]);
        OnPropertyChanged(nameof(SearchStatus));
    }

    private void SelectItem(FileSystemItem item)
    {
        // Expande todos los padres para asegurarte de que sea visible en el TreeView.
        ExpandParents(item);


        // Actualiza la selección del TreeView (suponiendo que tienes un SelectedItem enlazado).
        // Si no lo tienes, podrías exponer un evento o propiedad SelectedItem.
    }

    private void ExpandParents(FileSystemItem item)
    {
        if (item.Parent != null)
        {
            item.Parent.IsExpanded = true;
            ExpandParents(item.Parent);
        }
    }

    private async Task SearchFileSystemAsync(string query)
    {
        _searchResults.Clear();
        _currentResultIndex = -1;

        if (string.IsNullOrWhiteSpace(query))
        {
            OnPropertyChanged(nameof(SearchStatus));
            return;
        }

        foreach (var root in RootItems)
        {
            await SearchRecursiveAsync(root.FullPath, query);
        }

        if (_searchResults.Any())
        {
            _currentResultIndex = 0;
            SelectItem(_searchResults[_currentResultIndex]);
        }

        OnPropertyChanged(nameof(SearchStatus));
    }

    private async Task SearchRecursiveAsync(string path, string query)
    {
        try
        {
            // Archivos
            foreach (var file in Directory.GetFiles(path))
            {
                if (Path.GetFileName(file).Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    // Encuentra o crea FileSystemItem correspondiente
                    var item = await FindOrCreateItemAsync(file);
                    _searchResults.Add(item);
                }
            }

            // Subdirectorios
            foreach (var dir in Directory.GetDirectories(path))
            {
                if (Path.GetFileName(dir).Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    var item = await FindOrCreateItemAsync(dir, isDirectory: true);
                    _searchResults.Add(item);
                }

                // Recurse
                await SearchRecursiveAsync(dir, query);
            }
        }
        catch
        {
            // Ignorar errores de acceso
        }
    }

    private async Task<FileSystemItem> FindOrCreateItemAsync(string fullPath, bool isDirectory = false)
    {
        // Busca en RootItems/Children; si no existe, créalo y conecta Parent
        var segments = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();

        FileSystemItem? parent = RootItems.FirstOrDefault(r =>
            fullPath.StartsWith(r.FullPath, StringComparison.OrdinalIgnoreCase));

        if (parent == null)
        {
            // Si no hay root adecuado, créalo
            parent = new FileSystemItem
            {
                Name = Path.GetPathRoot(fullPath),
                FullPath = Path.GetPathRoot(fullPath),
                IsDirectory = true
            };
            RootItems.Add(parent);
        }

        var current = parent;
        string accumPath = current.FullPath;

        for (int i = 1; i < segments.Length; i++)
        {
            accumPath = Path.Combine(accumPath, segments[i]);
            var existing = current.Children.FirstOrDefault(c =>
                c.FullPath.Equals(accumPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                current = existing;
            }
            else
            {
                var child = new FileSystemItem
                {
                    Name = segments[i],
                    FullPath = accumPath,
                    IsDirectory = (i < segments.Length - 1) || isDirectory
                };
                child.Parent = current;
                current.Children.Add(child);
                current = child;
            }
        }

        return current;
    }

    public void CancelSearch()
    {
        SearchCancellationTokenSource?.Cancel();
    }
    
    public bool CancelSearchCanExecute()
    {
        return _searchCancellationTokenSource != null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}