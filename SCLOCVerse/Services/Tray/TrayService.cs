using H.NotifyIcon;
using SCLOCVerse.Interfaces;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace SCLOCVerse.Services.Tray
{
    /// <summary>
    /// Реалізація системного трея на базі H.NotifyIcon.TaskbarIcon.
    /// Іконка завантажується з WPF Resource (pack URI) — app_icon.ico.
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
                Icon = LoadIcon()
            };

            _taskbarIcon.TrayMouseDoubleClick += (s, e) => Raise(ShowRequested);
            _taskbarIcon.ContextMenu = BuildContextMenu();

            // Критично для програмного створення: без XAML-дерева TaskbarIcon не додає
            // іконку в системний трей автоматично. ForceCreate примусово реєструє її
            // в tray area оболонки Windows. Без цього виклику трей залишається порожнім.
            // enablesEfficiencyMode: false — уникаємо конфліктів з WPF-диспетчером.
            _taskbarIcon.ForceCreate(enablesEfficiencyMode: false);
            Trace.WriteLine("[TrayService] TaskbarIcon created and ForceCreate called.");
        }

        /// <summary>
        /// Завантажує іконку з WPF Resource (app_icon.ico підключений як Resource у .csproj).
        /// Pack URI використовує GetResourceStream — це стандартний WPF-спосіб для Resource-файлів.
        /// </summary>
        private static System.Drawing.Icon LoadIcon()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/app_icon.ico", UriKind.Absolute);
                var resourceInfo = Application.GetResourceStream(uri);
                if (resourceInfo == null)
                {
                    Trace.WriteLine("[TrayService] app_icon.ico resource not found, fallback to default icon.");
                    return System.Drawing.SystemIcons.Application;
                }

                using var stream = resourceInfo.Stream;
                return new System.Drawing.Icon(stream);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[TrayService] Icon load failed: {ex.Message}. Fallback to default icon.");
                return System.Drawing.SystemIcons.Application;
            }
        }

        /// <summary>
        /// Будує контекстне меню трея: "Показати", "Перевірити оновлення", "Вийти".
        /// </summary>
        private ContextMenu BuildContextMenu()
        {
            var menu = new ContextMenu();

            var showItem = new MenuItem { Header = "Показати" };
            showItem.Click += (s, e) => Raise(ShowRequested);
            menu.Items.Add(showItem);

            var checkItem = new MenuItem { Header = "Перевірити оновлення" };
            checkItem.Click += (s, e) => Raise(CheckUpdatesRequested);
            menu.Items.Add(checkItem);

            menu.Items.Add(new Separator());

            var exitItem = new MenuItem { Header = "Вийти" };
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

