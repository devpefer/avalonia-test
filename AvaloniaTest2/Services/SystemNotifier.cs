using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AvaloniaTest2.Interfaces;
#if WINDOWS
using System.Windows.Forms;
using System.Drawing;
#endif

namespace AvaloniaTest2.Services
{
    public class SystemNotifier : ISystemNotifier, IDisposable
    {
#if WINDOWS
        private NotifyIcon? _notifyIcon;
#endif

        public void Show(string title, string message)
        {
#if WINDOWS
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (_notifyIcon == null)Application.DoEvents();
                {
                    _notifyIcon = new NotifyIcon
                    {
                        Icon = SystemIcons.Application,
                        Visible = true
                    };
                }

                _notifyIcon.BalloonTipTitle = title;
                _notifyIcon.BalloonTipText = message;
                _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;Application.DoEvents();
                _notifyIcon.ShowBalloonTip(5000);
                Application.DoEvents();
                return;
            }
#endif
            // Linux
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try { Process.Start("notify-send", $"\"{title}\" \"{message}\""); } catch { }
            }
            // macOS
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                try { Process.Start("osascript", $"-e 'display notification \"{message}\" with title \"{title}\"'"); } catch { }
            }
        }

        public void Dispose()
        {
#if WINDOWS
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
#endif
        }
    }
}