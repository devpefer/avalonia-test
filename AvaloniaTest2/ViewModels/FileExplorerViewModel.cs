using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

namespace AvaloniaTest2.ViewModels;

public class FileExplorerViewModel : INotifyPropertyChanged
{
  public ICommand OpenFileCommand { get; }
  public ICommand OpenFolderCommand { get; }
  public ICommand CopyPathCommand { get; }
  public ICommand MoveToTrashCommand { get; }
  public ObservableCollection<FileSystemItem> RootItems { get; } = new();

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
    OpenFileCommand = new RelayCommand<FileSystemItem>(OpenFile);
    OpenFolderCommand = new RelayCommand<FileSystemItem>(OpenFolder);
    CopyPathCommand = new RelayCommand<FileSystemItem>(CopyPath);
    MoveToTrashCommand = new RelayCommand<FileSystemItem>(DeleteFileFromList);
    if (OperatingSystem.IsWindows())
    {
      foreach (var drive in DriveInfo.GetDrives())
        AddDriveRoot(drive.Name, drive.RootDirectory.FullName, drive);
    }
    else if (OperatingSystem.IsLinux())
    {
      foreach (var drive in DriveInfo.GetDrives())
        AddDriveRoot(drive.Name, drive.RootDirectory.FullName, drive);
    }
    else if (OperatingSystem.IsMacOS())
    {
      foreach (var drive in DriveInfo.GetDrives())
        AddDriveRoot(drive.Name, drive.RootDirectory.FullName, drive);
    }
  }


  private void ApplySortingToAll()
  {
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

  public void LoadChildren(FileSystemItem parent)
  {
    if (!parent.IsDirectory) return;
    parent.Children.Clear();

    try
    {
      // solo nombres
      var dirs = Directory.GetDirectories(parent.FullPath)
        .Select(d => new FileSystemItem
        {
          Name = Path.GetFileName(d),
          FullPath = d,
          IsDirectory = true,
          Size = -1 // desconocido
        }).ToList();

      var files = Directory.GetFiles(parent.FullPath)
        .Select(f => new FileSystemItem
        {
          Name = Path.GetFileName(f),
          FullPath = f,
          IsDirectory = false,
          Size = new FileInfo(f).Length
        }).ToList();

      foreach (var dir in dirs)
      {
        dir.Children.Add(new FileSystemItem { Name = "Cargando...", Size = -1 });
      }

      foreach (var dir in dirs)
        parent.Children.Add(dir);

      foreach (var file in files)
        parent.Children.Add(file);


      // calcula tamaños en background
      foreach (var dir in dirs)
      {
        Task.Run(() =>
        {
          long size = GetDirectorySizeSafe(new DirectoryInfo(dir.FullPath));
          dir.Size = size;
        });
      }
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

    bool confirm = await ShowConfirmationDialog(mainWindow, $"¿Deseas enviar el fichero a la papelera?\n{item.Name}");
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

  public event PropertyChangedEventHandler? PropertyChanged;

  private void OnPropertyChanged([CallerMemberName] string? name = null) =>
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}