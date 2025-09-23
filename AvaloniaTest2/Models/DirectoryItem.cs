using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace AvaloniaTest2.Models;

public class DirectoryItem : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public long Size { get; set; } = 0;

        private bool _isExpanded;
        private bool _isFile;

        public bool IsFile
        {
            get
            {
               return _isFile; 
            }
            set
            {
                _isFile = value;
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value) && !_isFile && !IsLoaded)
                    LoadChildrenAsync();
            }
        }

        private bool _isLoaded;

        public bool IsLoaded
        {
            get => _isLoaded;
            set => SetProperty(ref _isLoaded, value);
        }

        public ObservableCollection<DirectoryItem> Children { get; set; } = new ObservableCollection<DirectoryItem>();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        public async Task LoadChildrenAsync()
        {
            if (IsLoaded || IsFile) return;
            IsLoaded = true;

            long totalSize = 0;
            try
            {
                var files = Directory.GetFiles(Path);
                foreach (var file in files)
                {
                    var fi = new FileInfo(file);
                    var fileItem = new DirectoryItem
                    {
                        Name = fi.Name,
                        Path = file,
                        Size = fi.Length,
                        IsFile = true
                    };
                    totalSize += fi.Length;
                    Children.Add(fileItem);
                }

                var dirs = Directory.GetDirectories(Path);
                foreach (var dir in dirs)
                {
                    var dirItem = new DirectoryItem
                    {
                        Name = System.IO.Path.GetFileName(dir),
                        Path = dir,
                        IsFile = false
                    };
                    Children.Add(dirItem);
                }

                // Calcular tamaño total sumando tamaños de archivos y directorios cargados
                Size = totalSize;
            }
            catch
            {
                // Ignorar errores de permisos
            }
        }
    }
