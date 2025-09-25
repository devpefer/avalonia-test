using System.Threading.Tasks;
using Avalonia.Controls;

namespace AvaloniaTest2.Interfaces;

public interface IMessengerService
{
    Task<bool> ShowConfirmationDialog(Window parent, string message);
    Task<string?> ShowPasswordDialog(Window parent, string message);
    Task ShowMessageDialog(Window parent, string message);
}