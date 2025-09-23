using System.Collections.ObjectModel;
using System.IO;

namespace AvaloniaTest2.Models;

public class FileSystemItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public ObservableCollection<FileSystemItem> Children { get; set; } = new();
    public long Size { get; set; }

    public string DisplaySize => Size < 0 ? "" : FormatSize(Size);

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:F1} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:F1} MB";
        double gb = mb / 1024.0;
        return $"{gb:F2} GB";
    }
}