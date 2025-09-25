#if WINDOWS
using System;
using Windows.UI.Notifications;

namespace AvaloniaTest2.Services
{
    /// <summary>
    /// Helper para mostrar ToastNotifications desde aplicaciones de escritorio Windows.
    /// Copiado/adaptado de ejemplos oficiales de Microsoft.
    /// </summary>
    public static class DesktopNotificationManagerCompat
    {
        private static string? _appId;

        /// <summary>
        /// Registra un Application User Model ID (AUMID) para tu app.
        /// </summary>
        public static void RegisterAumid(string aumid)
        {
            _appId = aumid;
        }

        /// <summary>
        /// Crea un ToastNotifier para tu app.
        /// </summary>
        public static ToastNotifier CreateToastNotifier(string? aumid = null)
        {
            if (aumid != null)
                _appId = aumid;

            if (string.IsNullOrEmpty(_appId))
                throw new InvalidOperationException("Debes registrar un AUMID antes de usar CreateToastNotifier.");

            return ToastNotificationManager.CreateToastNotifier(_appId);
        }
    }
}
#endif