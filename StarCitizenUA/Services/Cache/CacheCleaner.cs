using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StarCitizenUA.Services.Cache
{
    public class CacheCleaner
    {
        private readonly CacheCleanupOptions _options;

        public CacheCleaner(CacheCleanupOptions options)
        {
            _options = options;
        }

        internal static bool IsShaderCacheDirectoryName(string? directoryName)
        {
            if (string.IsNullOrWhiteSpace(directoryName))
                return false;

            var lower = directoryName.ToLowerInvariant();
            if (lower.Contains("._del_"))
                return false;

            var normalizedBuilder = new System.Text.StringBuilder(lower.Length);
            foreach (var ch in lower)
            {
                if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_' || ch == ',')
                    continue;
                normalizedBuilder.Append(ch);
            }

            var normalized = normalizedBuilder.ToString();
            var hasStarCitizen = normalized.Contains("starcitizen", StringComparison.Ordinal);
            var hasScAlpha = normalized.Contains("scalpha", StringComparison.Ordinal) || lower.Contains("sc-alpha-", StringComparison.Ordinal);

            return hasStarCitizen && hasScAlpha;
        }

        public async Task ClearAllAsync(ShaderCacheInspection inspection, CancellationToken cancellationToken = default)
        {
            foreach (var entry in inspection.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DeleteEntryAsync(entry, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task ClearOldAsync(ShaderCacheInspection inspection, CancellationToken cancellationToken = default)
        {
            if (inspection.Latest == null)
                return;

            foreach (var entry in inspection.Entries.Where(e => !ReferenceEquals(e, inspection.Latest)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DeleteEntryAsync(entry, cancellationToken).ConfigureAwait(false);
            }
        }

        private Task DeleteEntryAsync(ShaderCacheEntry entry, CancellationToken cancellationToken)
        {
            return Task.Run(() => DeleteEntry(entry, cancellationToken), cancellationToken);
        }

        private void DeleteEntry(ShaderCacheEntry entry, CancellationToken cancellationToken)
        {
            var directory = new DirectoryInfo(entry.FullPath);
            if (!directory.Exists)
                return;

            cancellationToken.ThrowIfCancellationRequested();

            var tempPath = Path.Combine(directory.Parent?.FullName ?? _options.CacheRootPath, $"{directory.Name}.cleanup.{Guid.NewGuid():N}");
            Directory.Move(directory.FullName, tempPath);

            NormalizeAttributes(tempPath);

            var attempts = Math.Max(1, _options.DeleteRetryCount);
            Exception? lastError = null;
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    Directory.Delete(tempPath, true);
                    lastError = null;
                    break;
                }
                catch (IOException ex)
                {
                    lastError = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastError = ex;
                }

                if (attempt < attempts - 1)
                {
                    Task.Delay(_options.DeleteRetryDelay, cancellationToken).GetAwaiter().GetResult();
                }
            }

            if (lastError != null && Directory.Exists(tempPath))
            {
                throw lastError;
            }
        }

        private static void NormalizeAttributes(string path)
        {
            if (!Directory.Exists(path))
                return;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(directory, FileAttributes.Normal);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
