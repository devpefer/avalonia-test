using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using AvaloniaTest2.Interfaces;
using CommunityToolkit.WinUI.Notifications;
#if WINDOWS
using CommunityToolkit.WinUI.Notifications;
#endif

namespace AvaloniaTest2.Services
{
    public class SystemNotifier : ISystemNotifier
    {
        private WindowNotificationManager? _inAppManager;
        private WindowNotificationManager? _systemManager;

        private WindowNotificationManager GetInAppManager()
        {
            if (_inAppManager != null) return _inAppManager;

            if (Application.Current.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime ||
                lifetime.MainWindow == null)
                throw new InvalidOperationException("No hay ventana principal disponible");

            _inAppManager = new WindowNotificationManager(lifetime.MainWindow)
            {
                Position = NotificationPosition.BottomRight,
                MaxItems = 3
            };
            return _inAppManager;
        }

        private WindowNotificationManager GetSystemManager()
        {
            if (_systemManager != null) return _systemManager;

            if (Application.Current.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime ||
                lifetime.MainWindow == null)
                throw new InvalidOperationException("No hay ventana principal disponible");

            // AppNotificationManager puede usar notificaciones del sistema cuando la plataforma lo soporte
            _systemManager = new WindowNotificationManager(lifetime.MainWindow)
            {
                Position = NotificationPosition.BottomRight,
                MaxItems = 3
            };
            return _systemManager;
        }

        /// <summary>
        /// Notificación in-app (dentro de la app)
        /// </summary>
        public void ShowInApp(string title, string message)
        {
            var manager = GetInAppManager();
            manager.Show(new Avalonia.Controls.Notifications.Notification(
                title,
                message,
                NotificationType.Information,
                TimeSpan.FromSeconds(5)
            ));
        }

        /// <summary>
        /// Notificación a nivel de sistema (si la plataforma lo soporta)
        /// </summary>
        public void ShowSystem(string title, string message)
        {
#if WINDOWS
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Notificación en Windows
                var toastContent = new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message)
                    .GetToastContent();
                DesktopNotificationManagerCompat.CreateToastNotifier("MiAppAvalonia")
                    .Show(toastContent.CreateNotification());
            }
#else
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("notify-send", $"\"{title}\" \"{message}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("osascript", $"-e 'display notification \"{message}\" with title \"{title}\"'");
            }
#endif
        }
    }
}