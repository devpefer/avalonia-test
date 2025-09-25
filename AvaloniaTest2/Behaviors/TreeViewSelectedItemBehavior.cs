using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using AvaloniaTest2.Models;

namespace AvaloniaTest2.Behaviors;

public static class TreeViewSelectedItemBehavior
{
    public static readonly AttachedProperty<FileSystemItem?> SelectedItemProperty =
        AvaloniaProperty.RegisterAttached<TreeView, FileSystemItem?>(
            "SelectedItem", typeof(TreeViewSelectedItemBehavior));

    public static FileSystemItem? GetSelectedItem(TreeView treeView) =>
        treeView.GetValue(SelectedItemProperty);

    public static void SetSelectedItem(TreeView treeView, FileSystemItem? value) =>
        treeView.SetValue(SelectedItemProperty, value);

    static TreeViewSelectedItemBehavior()
    {
        SelectedItemProperty.Changed.AddClassHandler<TreeView>((tv, e) =>
        {
            if (e.NewValue is FileSystemItem item)
            {
                // Expandir nodos padres para que sea visible
                ExpandToItem(tv, item);
            }
        });
    }

    private static void ExpandToItem(TreeView treeView, FileSystemItem item)
    {
        var stack = new Stack<FileSystemItem>();
        var current = item.Parent;
        while (current != null)
        {
            stack.Push(current);
            current = current.Parent;
        }

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            node.IsExpanded = true;
        }

        // Aquí puedes agregar lógica para seleccionar visualmente
        // pero Avalonia TreeView no soporta selección directa de nodo sin control personalizado
    }
}
