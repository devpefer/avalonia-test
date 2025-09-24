using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaTest2.Models;
using System.Collections.ObjectModel;
using AvaloniaTest2.ViewModels;

namespace AvaloniaTest2.Views;

public partial class SearchResults : Window
{
    public SearchResults(string fileName)
    {
        InitializeComponent();
        var vm = new SearchResultsViewModel(this);
        DataContext = vm;
        _ = vm.StartSearchAsync(fileName);
    }
}