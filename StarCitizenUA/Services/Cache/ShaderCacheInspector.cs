using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StarCitizenUA.Services.Cache
{
    public class ShaderCacheInspector
    {
        private readonly CacheCleanupOptions _options;

        public ShaderCacheInspector(CacheCleanupOptions options)
        {
            _options = options;
        }

        public Task<ShaderCacheInspection> InspectAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() => InspectInternal(cancellationToken), cancellationToken);
        }

        private ShaderCacheInspection InspectInternal(CancellationToken cancellationToken)
        {
            var entries = new List<ShaderCacheEntry>();
            if (!Directory.Exists(_options.CacheRootPath))
                return new ShaderCacheInspection(_options, entries);

            cancellationToken.ThrowIfCancellationRequested();

            TryAddEntry(entries, new DirectoryInfo(Path.Combine(_options.CacheRootPath, _options.CacheRelativePath)), cancellationToken);

            foreach (var environmentDir in EnumerateDirectoriesSafe(_options.CacheRootPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var shadersDir = new DirectoryInfo(Path.Combine(environmentDir.FullName, _options.CacheRelativePath));
                if (string.Equals(shadersDir.FullName, Path.Combine(_options.CacheRootPath, _options.CacheRelativePath), StringComparison.OrdinalIgnoreCase))
                    continue;
                TryAddEntry(entries, shadersDir, cancellationToken);
            }

            return new ShaderCacheInspection(_options, entries);
        }

        private void TryAddEntry(ICollection<ShaderCacheEntry> entries, DirectoryInfo directory, CancellationToken cancellationToken)
        {
            if (!directory.Exists)
                return;

            var (size, lastWriteTimeUtc) = CalculateDirectoryMetrics(directory, cancellationToken);
            var displayName = DetermineDisplayName(directory);
            entries.Add(new ShaderCacheEntry(displayName, directory.FullName, size, lastWriteTimeUtc));
        }

        private string DetermineDisplayName(DirectoryInfo shadersDirectory)
        {
            var current = shadersDirectory.Parent;
            while (current != null)
            {
                if (string.Equals(current.Parent?.FullName, _options.CacheRootPath, StringComparison.OrdinalIgnoreCase))
                    return current.Name;

                current = current.Parent;
            }

            return shadersDirectory.Parent?.Name ?? shadersDirectory.Name;
        }

        private static (long size, DateTime lastWriteUtc) CalculateDirectoryMetrics(DirectoryInfo directory, CancellationToken cancellationToken)
        {
            long size = 0;
            DateTime lastWrite = directory.LastWriteTimeUtc;
            foreach (var file in EnumerateFilesSafe(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    size += file.Length;
                    if (file.LastWriteTimeUtc > lastWrite)
                        lastWrite = file.LastWriteTimeUtc;
                }
                catch (FileNotFoundException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }

            foreach (var dir in EnumerateSubdirectoriesSafe(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (dir.LastWriteTimeUtc > lastWrite)
                        lastWrite = dir.LastWriteTimeUtc;
                }
                catch (IOException)
                {
                }
            }

            return (size, lastWrite);
        }

        private static IEnumerable<FileInfo> EnumerateFilesSafe(DirectoryInfo root)
        {
            var stack = new Stack<DirectoryInfo>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                DirectoryInfo current = stack.Pop();
                FileInfo[] files;
                try
                {
                    files = current.GetFiles();
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var file in files)
                    yield return file;

                DirectoryInfo[] subDirs;
                try
                {
                    subDirs = current.GetDirectories();
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var subDir in subDirs)
                    stack.Push(subDir);
            }
        }

        private static IEnumerable<DirectoryInfo> EnumerateDirectoriesSafe(string path)
        {
            try
            {
                return new DirectoryInfo(path).GetDirectories();
            }
            catch (DirectoryNotFoundException)
            {
                return Enumerable.Empty<DirectoryInfo>();
            }
            catch (UnauthorizedAccessException)
            {
                return Enumerable.Empty<DirectoryInfo>();
            }
        }

        private static IEnumerable<DirectoryInfo> EnumerateSubdirectoriesSafe(DirectoryInfo root)
        {
            try
            {
                return root.GetDirectories();
            }
            catch (DirectoryNotFoundException)
            {
                return Enumerable.Empty<DirectoryInfo>();
            }
            catch (UnauthorizedAccessException)
            {
                return Enumerable.Empty<DirectoryInfo>();
            }
            catch (IOException)
            {
                return Enumerable.Empty<DirectoryInfo>();
            }
        }
    }
}
