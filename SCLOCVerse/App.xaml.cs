using SCLOCVerse.Composition;
using SCLOCVerse.Controls.Dialogs;
using SCLOCVerse.Services.ApplicationUpdate;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace SCLOCVerse
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static Mutex? _singleInstanceMutex;
        private const string SingleInstanceMutexName = "SCLOCVerse_SingleInstanceMutex";
        private AppCompositionRoot? _compositionRoot;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Перевірка single instance через локальний Mutex.
            bool createdNew;
            try
            {
                _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
            }
            catch (AbandonedMutexException)
            {
                // Попередній екземпляр аварійно завершився, mutex звільнено.
                // Поточний процес стає першим екземпляром.
                createdNew = true;
                _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out _);
            }

            if (!createdNew)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;

                BaseDialog.Show(new DialogOptions
                {
                    Type = DialogType.Info,
                    Title = "SCLOC-Verse",
                    Message = "Програма вже запущена. Ви можете мати лише один активний екземпляр.",
                    Buttons = MessageBoxButton.OK
                });

                Shutdown();
                return;
            }

            // Автоматична міграція User Settings після оновлення версії.
            MigrateSettingsIfNeeded();

            _compositionRoot = new AppCompositionRoot();
            var window = _compositionRoot.CreateMainWindow();
            MainWindow = window;

            // Закриття головного вікна має завершувати застосунок, навіть якщо
            // відкритий Hangar overlay. OnExit закриє overlay через Dispose.
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            window.Show();
        }

        private static void MigrateSettingsIfNeeded()
        {
            var currentVersion = GetCurrentVersionString();
            var lastAppVersion = Settings.Default.LastAppVersion;

            if (string.Equals(lastAppVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
                return;

            // Спроба стандартної міграції Application Settings.
            try
            {
                Settings.Default.Upgrade();
            }
            catch
            {
                // Ігноруємо помилки міграції, щоб не блокувати запуск додатка.
            }

            // Гарантуємо значення за замовчуванням для каналу оновлень.
            if (string.IsNullOrWhiteSpace(Settings.Default.UpdateChannel))
            {
                Settings.Default.UpdateChannel = "Stable";
            }

            Settings.Default.LastAppVersion = currentVersion;
            Settings.Default.UpgradeRequired = false;
            Settings.Default.Save();
        }

        private static string GetCurrentVersionString()
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(App).Assembly;
            var version = assembly.GetName().Version;

            return version?.ToString() ?? new Version(0, 0, 0, 0).ToString();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Спочатку звільняємо всі фонові ресурси: таймери, HttpListener,
            // Supabase refresh timer тощо. Інакше Dispatcher залишиться живим
            // і OnExit зависне на мережевих викликах.
            try
            {
                _compositionRoot?.Dispose();
            }
            catch
            {
                // Не блокуємо вихід при помилках dispose.
            }

            try
            {
                if (_compositionRoot?.AuthCompositionRoot?.SessionTrackerService is { } sessionTracker)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    // EndSessionAsync виконує мережевий виклик Supabase Postgrest. На UI-потоці з
                    // DispatcherSynchronizationContext це sync-over-async deadlock (MakeRequest<T>
                    // усередині Postgrest не має ConfigureAwait(false)). Винесення на threadpool
                    // прибирає SynchronizationContext → дедлок неможливий. GetResult() чекає
                    // завершення телеметрії (≤3с за CTS), щоб надійно зафіксувати ended_at.
                    Task.Run(async () => await sessionTracker.EndSessionAsync(cts.Token))
                        .GetAwaiter()
                        .GetResult();
                }
            }
            catch (Exception ex)
            {
                // Не блокуємо вихід при помилках телеметрії або таймауті, але фіксуємо причину.
                Debug.WriteLine($"[App.OnExit] EndSessionAsync failed: {ex}");
            }

            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch
            {
                // Ігноруємо помилки при звільненні mutex.
            }

            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
