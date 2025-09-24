using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaTest2.Interfaces;

namespace AvaloniaTest2.Services;

public class MessengerService : IMessengerService
{
    public async Task<bool> ShowConfirmationDialog(Window parent, string message)
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


    public async Task<string?> ShowPasswordDialog(Window parent, string message)
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

    public async Task ShowMessageDialog(Window parent, string message)
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