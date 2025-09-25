using System;
using System.Linq;
using AvaloniaTest2.Enums;
using AvaloniaTest2.Models;

namespace AvaloniaTest2.Helpers;

public static class FileSystemItemExtensions
{
    public static void SortRecursively(this FileSystemItem item, SortMode mode)
    {
        if (item.Children.Count == 0) return;

        var sorted = mode switch
        {
            SortMode.SizeDesc => item.Children.OrderByDescending(c => c.LogicalSize).ToList(),
            _ => item.Children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList()
        };

        item.Children.Clear();
        foreach (var child in sorted)
        {
            item.Children.Add(child);
            if (child.IsDirectory)
                child.SortRecursively(mode);
        }
    }
}
