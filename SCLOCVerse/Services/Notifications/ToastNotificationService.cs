using CommunityToolkit.WinUI.Notifications;
using SCLOCVerse.Helpers;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Notifications;
using System.Diagnostics;

namespace SCLOCVerse.Services.Notifications
{
    /// <summary>
    /// Реалізація OS-сповіщень через CommunityToolkit.WinUI.Notifications
    /// (ToastContentBuilder + ToastNotificationManagerCompat).
    ///
    /// Для unpackaged Win32-застосунків Compat-обгортка автоматично:
    ///   - генерує AUMID на основі шляху до .exe
    ///   - створює .lnk ярлик у Start Menu (потрібен для Notification Center)
    ///   - реєструє AUMID у HKCU\Software\Classes\AppUserModelId\
    ///
    /// UTF-8 гарантується ToastContentBuilder (серіалізує XML у UTF-8) —
    /// українські літерали без mojibake (AGENTS.md §P0).
    /// </summary>
    public class ToastNotificationService : IToastNotificationService
    {
        public void Show(ToastNotification notification)
        {
            // Універсальний метод Show приймає DTO з будь-яким типом повідомлення.
            // ToastContentBuilder серіалізує XML у UTF-8 автоматично.
            var builder = new ToastContentBuilder()
                .AddText(notification.Title)
                .AddText(notification.Message);

            // Arguments для маршрутизації toast-кліку (App.xaml.cs OnActivated).
            // SourceTag формується в MainWindow.PresentNotification через ToastSources константи.
            if (!string.IsNullOrWhiteSpace(notification.SourceTag))
            {
                builder.AddArgument(ToastArgumentKeys.Source, notification.SourceTag);
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(notification.SourceTag))
                {
                    // SourceTag використовується для групування в Notification Center
                    // та для dedup. CustomizeToast делегат дозволяє встановити Tag
                    // на нативному Windows.UI.Notifications.ToastNotification.
                    builder.Show(toast =>
                    {
                        toast.Tag = notification.SourceTag;
                        toast.Group = notification.SourceTag;
                    });
                }
                else
                {
                    builder.Show();
                }
            }
            catch (Exception ex)
            {
                // Toast не повинен валити застосунок. Логуємо для діагностики.
                Debug.WriteLine($"[ToastNotificationService] Show failed: {ex.Message}");
            }
        }
    }
}
