using H.NotifyIcon;
using SCLOCVerse.Interfaces;
using System.IO;
using System.Windows;

namespace SCLOCVerse.Services.Tray
{
    /// <summary>
    /// Реалізація системного трея на базі H.NotifyIcon.TaskbarIcon.
    /// Іконка використовує app_icon.ico як Resource (вже підключений у .csproj).
    /// Усі події маршалінгуються в UI-потік через Application.Current.Dispatcher.
    /// </summary>
    public class TrayService : ITrayService
    {
        private TaskbarIcon? _taskbarIcon;
        private bool _disposed;

        public bool IsInitialized => _taskbarIcon != null;

        public event EventHandler? ShowRequested;
        public event EventHandler? CheckUpdatesRequested;
        public event EventHandler? ExitRequested;

        public void Initialize()
        {
            if (_taskbarIcon != null)
                return;

            // Створюємо TaskbarIcon програмно — у стилі автора (ручна композиція, без XAML-ресурсів).
            _taskbarIcon = new TaskbarIcon
            {
                ToolTipText = "SCLOC-Verse",
                IconSource = LoadIconSource()
            };

            _taskbarIcon.TrayMouseDoubleClick += (s, e) => Raise(ShowRequested);
            _taskbarIcon.ContextMenu = BuildContextMenu();
        }

        /// <summary>
        /// Завантажує іконку з-packaged resource.
        /// app_icon.ico підключений як Resource у .csproj.
        /// </summary>
        private static System.Windows.Media.ImageSource LoadIconSource()
        {
            // Pack URI для ресурсу збірки.
            var uri = new Uri("pack://application:,,,/app_icon.ico", UriKind.Absolute);
            return System.Windows.Media.Imaging.BitmapFrame.Create(uri);
        }

        /// <summary>
        /// Будує контекстне меню трея: "Показати", "Перевірити оновлення", "Вийти".
        /// </summary>
        private System.Windows.Controls.ContextMenu BuildContextMenu()
        {
            var menu = new System.Windows.Controls.ContextMenu();

            var showItem = new System.Windows.Controls.MenuItem { Header = "Показати" };
            showItem.Click += (s, e) => Raise(ShowRequested);
            menu.Items.Add(showItem);

            var checkItem = new System.Windows.Controls.MenuItem { Header = "Перевірити оновлення" };
            checkItem.Click += (s, e) => Raise(CheckUpdatesRequested);
            menu.Items.Add(checkItem);

            menu.Items.Add(new System.Windows.Controls.Separator());

            var exitItem = new System.Windows.Controls.MenuItem { Header = "Вийти" };
            exitItem.Click += (s, e) => Raise(ExitRequested);
            menu.Items.Add(exitItem);

            return menu;
        }

        public void ShowMainWindow()
        {
            Raise(ShowRequested);
        }

        /// <summary>
        /// Піднімає подію в UI-потоці.
        /// Tray-контрол H.NotifyIcon може викликати з фонових потоків,
        /// тому маршалінг у Dispatcher потрібен для безпечної роботи з UI.
        /// </summary>
        private void Raise(EventHandler? handler)
        {
            if (handler == null)
                return;

            if (Application.Current?.Dispatcher?.CheckAccess() == true)
                handler(this, EventArgs.Empty);
            else
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() => handler(this, EventArgs.Empty)));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_taskbarIcon != null)
            {
                _taskbarIcon.Dispose();
                _taskbarIcon = null;
            }
        }
    }
}
