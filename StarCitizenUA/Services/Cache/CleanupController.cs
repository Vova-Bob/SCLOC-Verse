using StarCitizenUA.Controls;
using StarCitizenUA.Interfaces;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace StarCitizenUA.Services.Cache
{
    public class CleanupController
    {
        private readonly ShaderCacheInspector _inspector;
        private readonly CacheCleaner _cleaner;
        private readonly IToastService _toastService;
        private readonly Dispatcher _dispatcher;
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public CleanupController(ShaderCacheInspector inspector, CacheCleaner cleaner, IToastService toastService, Dispatcher? dispatcher = null)
        {
            _inspector = inspector;
            _cleaner = cleaner;
            _toastService = toastService;
            _dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        public static CleanupController CreateDefault(IToastService toastService, Dispatcher? dispatcher = null)
        {
            var options = new CacheCleanupOptions();
            var inspector = new ShaderCacheInspector(options);
            var cleaner = new CacheCleaner(options);
            return new CleanupController(inspector, cleaner, toastService, dispatcher);
        }

        public async Task RunStartupPromptAsync(CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var inspection = await _inspector.InspectAsync(cancellationToken).ConfigureAwait(false);
                if (!ShouldShowStartupPrompt(inspection))
                    return;

                await HandlePromptAsync(inspection, CleanupScenario.Startup, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task HandleManualCleanupAsync(CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var inspection = await _inspector.InspectAsync(cancellationToken).ConfigureAwait(false);
                await HandlePromptAsync(inspection, CleanupScenario.Manual, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private bool ShouldShowStartupPrompt(ShaderCacheInspection inspection)
        {
            if (!inspection.HasCache)
                return false;

            return inspection.LatestTooLarge || inspection.HasOlder || inspection.HasBigDirectories;
        }

        private async Task HandlePromptAsync(ShaderCacheInspection inspection, CleanupScenario scenario, CancellationToken cancellationToken)
        {
            try
            {
                if (!inspection.HasCache)
                {
                    if (scenario == CleanupScenario.Manual)
                        await ShowToastAsync("Кеш уже порожній.").ConfigureAwait(false);

                    return;
                }

                if (inspection.LatestTooLarge)
                {
                    var message = BuildLatestTooLargeMessage(inspection);
                    var result = await ShowDialogAsync(message, "Очищення кешу", MessageBoxButton.YesNo, MessageBoxImage.Warning).ConfigureAwait(false);
                    if (result == MessageBoxResult.Yes)
                    {
                        await _cleaner.ClearAllAsync(inspection, cancellationToken).ConfigureAwait(false);
                        await ShowToastAsync("Кеш шейдерів очищено.").ConfigureAwait(false);
                    }

                    return;
                }

                var generalMessage = BuildGeneralPrompt(inspection);
                var generalResult = await ShowDialogAsync(generalMessage, "Очищення кешу", MessageBoxButton.YesNoCancel, MessageBoxImage.Question).ConfigureAwait(false);

                switch (generalResult)
                {
                    case MessageBoxResult.Yes:
                        await _cleaner.ClearOldAsync(inspection, cancellationToken).ConfigureAwait(false);
                        await ShowToastAsync("Старі кеші видалено.").ConfigureAwait(false);
                        break;
                    case MessageBoxResult.No:
                        await _cleaner.ClearAllAsync(inspection, cancellationToken).ConfigureAwait(false);
                        await ShowToastAsync("Кеш шейдерів очищено.").ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await ShowDialogAsync($"Не вдалося виконати очищення кешу.\n{ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error).ConfigureAwait(false);
            }
        }

        private string BuildLatestTooLargeMessage(ShaderCacheInspection inspection)
        {
            var latest = inspection.Latest;
            if (latest == null)
                return "Останній кеш не знайдено.";

            var builder = new StringBuilder();
            builder.AppendLine("Останній кеш шейдерів занадто великий.");
            builder.AppendLine($"{latest.DisplayName}: {FormatBytes(latest.SizeBytes)} (поріг {FormatBytes(inspection.Options.LatestOkBytes)}).");
            builder.AppendLine();
            builder.AppendLine("Очистити весь кеш шейдерів?");
            return builder.ToString();
        }

        private string BuildGeneralPrompt(ShaderCacheInspection inspection)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Знайдено кеші шейдерів Star Citizen.");
            builder.AppendLine($"Загальний розмір: {FormatBytes(inspection.TotalBytes)}.");

            if (inspection.Latest != null)
                builder.AppendLine($"Останній кеш ({inspection.Latest.DisplayName}): {FormatBytes(inspection.Latest.SizeBytes)}.");

            if (inspection.HasOlder)
            {
                var older = inspection.Entries
                    .Where(e => !ReferenceEquals(e, inspection.Latest))
                    .Select(e => $"• {e.DisplayName} - {FormatBytes(e.SizeBytes)}");

                builder.AppendLine();
                builder.AppendLine("Старі кеші:");
                foreach (var line in older)
                    builder.AppendLine(line);
            }

            if (inspection.HasBigDirectories)
            {
                var big = inspection.Entries
                    .Where(e => e.SizeBytes > inspection.Options.BigDirectoryBytes)
                    .Select(e => $"• {e.DisplayName} - {FormatBytes(e.SizeBytes)}");

                builder.AppendLine();
                builder.AppendLine($"Папки понад {FormatBytes(inspection.Options.BigDirectoryBytes)}:");
                foreach (var line in big)
                    builder.AppendLine(line);
            }

            builder.AppendLine();
            builder.AppendLine("Оберіть, що очистити:");
            return builder.ToString();
        }

        private async Task<MessageBoxResult> ShowDialogAsync(string message, string caption, MessageBoxButton buttons, MessageBoxImage image)
        {
            if (_dispatcher.CheckAccess())
                return CacheCleanupDialog.ShowDialog(Application.Current?.MainWindow, caption, message, buttons, image);

            return await _dispatcher.InvokeAsync(() =>
                CacheCleanupDialog.ShowDialog(Application.Current?.MainWindow, caption, message, buttons, image)).Task.ConfigureAwait(false);
        }

        private async Task ShowToastAsync(string message)
        {
            async Task InnerAsync()
            {
                await _toastService.ShowToastAsync(message).ConfigureAwait(true);
            }

            if (_dispatcher.CheckAccess())
            {
                await InnerAsync().ConfigureAwait(true);
                return;
            }

            var operation = _dispatcher.InvokeAsync(InnerAsync);
            await (await operation.Task.ConfigureAwait(false)).ConfigureAwait(true);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0)
                return "0 Б";

            double size = bytes;
            var units = new[] { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            var unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return string.Format(CultureInfo.GetCultureInfo("uk-UA"), "{0:0.##} {1}", size, units[unitIndex]);
        }

        private enum CleanupScenario
        {
            Startup,
            Manual
        }
    }
}
