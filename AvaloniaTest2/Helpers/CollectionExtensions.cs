using System.Collections.ObjectModel;
using System.Linq;
using AvaloniaTest2.Enums;
using AvaloniaTest2.Models;

namespace AvaloniaTest2.Helpers;

public static class CollectionExtensions
{
    public static void SortBy(this ObservableCollection<FileSystemItem> collection, SortMode mode)
    {
        var sorted = mode == SortMode.SizeDesc
            ? collection.OrderByDescending(x => x.Size).ToList()
            : collection.OrderBy(x => x.Name).ToList();

        collection.Clear();
        foreach (var item in sorted)
            collection.Add(item);
    }
}