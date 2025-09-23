using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using AvaloniaTest2.Enums;
using AvaloniaTest2.Helpers;
using AvaloniaTest2.Models;

public class MainWindowViewModel : INotifyPropertyChanged
{
    public ObservableCollection<FileSystemItem> RootItems { get; } = new();
    public Array SortModes => Enum.GetValues(typeof(SortMode));

    private SortMode _selectedSort = SortMode.NameAsc;
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

    public MainWindowViewModel()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives())
                AddDriveRoot(drive.Name, drive.RootDirectory.FullName, drive);
        }
        else
            AddDriveRoot("/home/devpefer", "/home/devpefer", null);
    }

    private void ApplySortingToAll()
    {
        // Ordena la raíz
        var sortedRoot = SelectedSort switch
        {
            SortMode.SizeDesc => RootItems.OrderByDescending(r => r.Size).ToList(),
            _ => RootItems.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList()
        };

        RootItems.Clear();
        foreach (var root in sortedRoot)
        {
            RootItems.Add(root);
            root.SortRecursively(SelectedSort);
        }
    }

    private void AddDriveRoot(string name, string fullPath, DriveInfo? drive)
    {
        long size = -1;
        try { if (drive?.IsReady == true) size = drive.TotalSize; } catch { }

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

    public void LoadChildren(FileSystemItem parent)
    {
        if (!parent.IsDirectory) return;
        parent.Children.Clear();

        try
        {
            var dirs = Directory.GetDirectories(parent.FullPath).Select(d =>
            {
                var info = new DirectoryInfo(d);
                return new FileSystemItem
                {
                    Name = info.Name,
                    FullPath = d,
                    IsDirectory = true,
                    Size = GetDirectorySizeSafe(info)
                };
            });

            var files = Directory.GetFiles(parent.FullPath).Select(f =>
            {
                var fi = new FileInfo(f);
                return new FileSystemItem
                {
                    Name = fi.Name,
                    FullPath = f,
                    IsDirectory = false,
                    Size = fi.Length
                };
            });

            var children = dirs.Concat(files);
            children = SelectedSort switch
            {
                SortMode.SizeDesc => children.OrderByDescending(c => c.Size),
                _ => children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            };

            foreach (var child in children)
            {
                if (child.IsDirectory)
                    child.Children.Add(new FileSystemItem { Name = "Cargando...", Size = -1 });
                parent.Children.Add(child);
            }

            parent.Size = parent.Children.Sum(c => c.Size > 0 ? c.Size : 0);
        }
        catch { }
    }

    private long GetDirectorySizeSafe(DirectoryInfo dir)
    {
        long size = 0;
        try
        {
            foreach (var f in dir.GetFiles()) size += f.Length;
            foreach (var sub in dir.GetDirectories()) size += GetDirectorySizeSafe(sub);
        }
        catch { }
        return size;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
