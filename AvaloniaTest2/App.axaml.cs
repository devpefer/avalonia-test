using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaTest2.Services;
using AvaloniaTest2.ViewModels;
using AvaloniaTest2.Views;

namespace AvaloniaTest2;

public partial class App : Application
{
    private TrayIcon? _tray;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };

            if (OperatingSystem.IsWindows())
            {
                SetupTray(desktop);
            }

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow.Show();

            var floatingPanel = new FloatingPanel();
            floatingPanel.Show();
        }

        base.OnFrameworkInitializationCompleted();
        
        var systemNotifier = new SystemNotifier();
        systemNotifier.Show("Bienvenido", "App iniciada correctamente");
        AppDomain.CurrentDomain.ProcessExit += (s, e) => systemNotifier.Dispose();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private void SetupTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _tray = new TrayIcon
        {
            Icon = new WindowIcon("Assets/appicon.png"), // reemplaza con tu icono
            ToolTipText = "File Explorer",
            IsVisible = true
        };

        var menu = new NativeMenu();
        menu.Items.Add(new NativeMenuItem("Abrir"));
        menu.Items.Add(new NativeMenuItem("Salir"));

        _tray.Menu = menu;
    }
}