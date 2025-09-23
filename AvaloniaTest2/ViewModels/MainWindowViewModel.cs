using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using System.Linq;
using Avalonia.Data.Converters;
using AvaloniaTest2.Helpers;

namespace AvaloniaTest2.ViewModels
{
    public enum SortCriterion { Name, Size }
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

        public ICommand OpenFileCommand { get; }
        public ICommand CopyPathCommand { get; }

        public MainWindowViewModel()
        {
            OpenFileCommand = new RelayCommand<FileItem>(OpenFile);
            CopyPathCommand = new RelayCommand<FileItem>(CopyPath);

            LoadLargestFiles();
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        
        protected bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
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
                // ignorar fallos leves
            }
        }

        private void CopyPath(FileItem? item)
        {
        }

        private async void LoadLargestFiles()
        {
            IsLoading = true;
            LargestFiles.Clear();

            string rootPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"C:\" : "/home/devpefer";
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
    }
}