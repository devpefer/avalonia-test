using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaTest2.Helpers;
using Microsoft.VisualBasic.FileIO;

namespace AvaloniaTest2.ViewModels
{
    public enum SortCriterion
    {
        Name,
        Size
    }

    public class FileItem
    {
        public string Path { get; set; } = "";
        public long Size { get; set; }
        public string Name => System.IO.Path.GetFileName(Path);
    }

    public class MainWindowViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<string> SortOptions { get; } = new() { "Name", "Size" };
        private SortCriterion _sortBy = SortCriterion.Size;

        public SortCriterion SortBy
        {
            get => _sortBy;
            set
            {
                if (SetProperty(ref _sortBy, value))
                    ApplySorting();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private ObservableCollection<FileItem> _largestFiles = new();

        public ObservableCollection<FileItem> LargestFiles
        {
            get => _largestFiles;
            set => SetProperty(ref _largestFiles, value);
        }

        private bool _isLoading;

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand CopyPathCommand { get; }
        public ICommand MoveToTrashCommand { get; }

        public MainWindowViewModel()
        {
            RefreshCommand = new RelayCommand<FileItem>(RefreshFilesAsync);
            OpenFileCommand = new RelayCommand<FileItem>(OpenFile);
            OpenFolderCommand = new RelayCommand<FileItem>(OpenFolder);
            CopyPathCommand = new RelayCommand<FileItem>(CopyPath);
            MoveToTrashCommand = new RelayCommand<FileItem>(DeleteFileFromList);

            LoadLargestFiles();
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool SetProperty<T>(ref T field, T value,
            [System.Runtime.CompilerServices.CallerMemberName]
            string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        private void OpenFile(FileItem? item)
        {
            if (item == null) return;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start(new ProcessStartInfo("xdg-open", item.Path) { UseShellExecute = false });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start(new ProcessStartInfo("open", item.Path) { UseShellExecute = false });
                }
            }
            catch
            {
            }
        }

        private void OpenFolder(FileItem? item)
        {
            if (item == null) return;

            try
            {
                string? folderPath = Path.GetDirectoryName(item.Path);
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

        private async void CopyPath(FileItem? item)
        {
            if (item == null) return;

            await Dispatcher.UIThread.InvokeAsync(async () => { await CopiarAlPortapapeles(item.Path); });
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
        
        private async void DeleteFileFromList(FileItem? item)
        {
            if (item == null) return;

            var mainWindow =
                Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
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
                    LargestFiles.Remove(item);
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

        
        private bool MoveToTrash(FileItem item)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: P/Invoke a SHFileOperation
                try
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    return true;
                }
                catch { return false; }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux: usa gio trash si existe
                var psi = new ProcessStartInfo("gio", $"trash \"{item.Path}\"")
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
                string cmd = $"tell application \"Finder\" to delete POSIX file \"{item.Path}\"";
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


        private async void LoadLargestFiles()
        {
            IsLoading = true;
            LargestFiles.Clear();

            string rootPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"C:\" : "/";
            await FileAnalyzer.GetLargestFilesIncremental(rootPath, 10,
                f =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        LargestFiles.Add(new FileItem { Path = f.FullName, Size = f.Length });
                    });
                });

            IsLoading = false;
        }

        private void ApplySorting()
        {
            IEnumerable<FileItem> sorted = SortBy switch
            {
                SortCriterion.Name => LargestFiles.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase),
                SortCriterion.Size => LargestFiles.OrderByDescending(f => f.Size),
                _ => LargestFiles
            };

            LargestFiles = new ObservableCollection<FileItem>(sorted);
        }
        
        private async void RefreshFilesAsync(FileItem file)
        {
            IsLoading = true;
            LargestFiles.Clear();
            await FileAnalyzer.GetLargestFilesIncremental(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"C:\" : "/",
                10,
                f => Dispatcher.UIThread.Post(() =>
                {
                    LargestFiles.Add(new FileItem { Path = f.FullName, Size = f.Length });
                })
            );
            IsLoading = false;
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
    }
}