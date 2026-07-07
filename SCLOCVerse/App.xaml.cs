using SCLOCVerse.Composition;
using SCLOCVerse.Helpers;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.ApplicationInstance;
using SCLOCVerse.Models.Observability;
using SCLOCVerse.Services.UiPolicy;
using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using System.Web;
using System.Windows;

using CommunityToolkit.WinUI.Notifications;

namespace SCLOCVerse
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private AppCompositionRoot? _compositionRoot;

        /// <summary>
        /// При запуску з Windows Tray/Autostart передається цей прапорець —
        /// вікно стартує прихованим у трей, не показуючись на екрані.
        /// </summary>
        private const string MinimizedArg = "--minimized";

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Парсинг аргументів запуску. --minimized — старт у треї (для автозапуску).
            var startMinimized = Array.Exists(e.Args ?? Array.Empty<string>(),
                a => string.Equals(a, MinimizedArg, StringComparison.OrdinalIgnoreCase));

            // Автоматична міграція User Settings після оновлення версії.
            MigrateSettingsIfNeeded();

            _compositionRoot = new AppCompositionRoot();

            // Toast Activation — точка маршрутизації всіх системних подій.
            // App.xaml — природне місце для маршрутизації (як Startup/Exit/SessionEnding).
            // Toast клік → маршрутизація за Arguments (Етап F).
            // source=lia → ShowLiaAssistant (вікно + вкладка Assistant).
            // default (без arguments, або app-update/localization) → ShowMainWindow.
            ToastNotificationManagerCompat.OnActivated += toastArgs =>
            {
                // Маршалінг у UI-потік: OnActivated може викликатись з фонового потоку.
                Dispatcher.Invoke(() =>
                {
                    var args = ParseToastArguments(toastArgs.Argument);
                    var source = args[ToastArgumentKeys.Source];

                    if (source == ToastSources.Lia)
                        _compositionRoot?.ApplicationInstance.ShowLiaAssistant();
                    else
                        _compositionRoot?.ApplicationInstance.ShowMainWindow();
                });
            };

            // Single Instance: перевірка й IPC-активація.
            // Перший процес — продовжує запуск UI + піднімає pipe-сервер.
            // Другий процес  — передає команду Show першому й тихо завершується.
            if (!_compositionRoot.ApplicationInstance.IsFirstInstance)
            {
                await _compositionRoot.ApplicationInstance
                    .SignalExistingInstanceAsync(new InstanceCommand { Kind = InstanceCommandKind.Show })
                    .ConfigureAwait(true);

                // Звільняємо ресурси другого процесу перед виходом.
                _compositionRoot.Dispose();
                Shutdown();
                return;
            }

            // SCLOC Observability Platform — фіксуємо запуск (Slice 1).
            // Не блокує, не кидає (Конституція, Стаття 1/3). Pre-auth подія
            // буферується й відправляється після авторизації.
            _compositionRoot.Telemetry.Track("Application", "Start", "Started", level: TelemetryLevel.Diagnostic);

            // Політика взаємодії з UI: визначає, які UI-елементи дозволені.
            // --minimized → BackgroundUiPolicy (модальні діалоги/стартові промпти заборонені,
            // бо підняли б приховане вікно з трея). Інакше — InteractiveUiPolicy.
            IUiInteractionPolicy uiPolicy = startMinimized
                ? new BackgroundUiPolicy()
                : new InteractiveUiPolicy();

            var window = _compositionRoot.CreateMainWindow(uiPolicy);
            MainWindow = window;

            // Tray-режим: закриття головного вікна ховає його у трей, а не гасить
            // процес. Вихід — лише через tray-меню "Вийти" або явний Shutdown().
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            if (startMinimized)
            {
                // Старт у треї: вікно створене, але приховане. Tray-іконку
                // та обробники подій трея підключає MainWindow.xaml.cs.
                window.Hide();
            }
            else
            {
                window.Show();
            }

            // Перший процес піднімає pipe-сервер для прийому команд від повторних
            // запусків. Подія CommandReceived підключається в MainWindow.
            await _compositionRoot.ApplicationInstance.StartServerAsync().ConfigureAwait(true);
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

        /// <summary>
        /// Парсить toast-аргументи (URL-encoded key=value, розділені '&').
        /// Пріоритет: System.Web.HttpUtility.ParseQueryString (обробляє URL-encoding,
        /// '+' як пробіл, повторювані ключі). Не підтягує ASP.NET-залежності.
        /// Майбутнє розширення: args[ToastArgumentKeys.Version], args[ToastArgumentKeys.Environment].
        /// </summary>
        private static NameValueCollection ParseToastArguments(string? arguments)
        {
            if (string.IsNullOrEmpty(arguments))
                return new NameValueCollection();

            try
            {
                return HttpUtility.ParseQueryString(arguments);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Toast] Failed to parse arguments '{arguments}': {ex.Message}");
                return new NameValueCollection();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Спочатку звільняємо всі фонові ресурси: таймери, HttpListener,
            // Supabase refresh timer, pipe-сервер єдиного екземпляра тощо.
            // Інакше Dispatcher залишиться живим і OnExit зависне на мережевих
            // викликах. CompositionRoot.Dispose прибирає й Mutex/Pipe через
            // ApplicationInstanceService (reverse-order).
            try
            {
                _compositionRoot?.Dispose();
            }
            catch
            {
                // Не блокуємо вихід при помилках dispose.
            }

            base.OnExit(e);
        }
    }
}
