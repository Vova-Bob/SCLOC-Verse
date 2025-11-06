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

        public async Task<ShaderCacheInspection> InspectAsync(CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(_options.CacheRootPath))
                return new ShaderCacheInspection(_options, Array.Empty<ShaderCacheEntry>());

            var rootDirectory = new DirectoryInfo(_options.CacheRootPath);
            var candidates = GetCandidateDirectories(rootDirectory, cancellationToken).ToList();
            if (candidates.Count == 0)
                return new ShaderCacheInspection(_options, Array.Empty<ShaderCacheEntry>());

            var inspectionTasks = candidates.Select(directory => InspectDirectoryAsync(directory, cancellationToken)).ToArray();
            var results = await Task.WhenAll(inspectionTasks).ConfigureAwait(false);
            var entries = results.Where(entry => entry != null).Cast<ShaderCacheEntry>().ToList();

            return new ShaderCacheInspection(_options, entries);
        }

        private IEnumerable<DirectoryInfo> GetCandidateDirectories(DirectoryInfo root, CancellationToken cancellationToken)
        {
            DirectoryInfo[] directories;
            try
            {
                directories = root.GetDirectories();
            }
            catch (IOException)
            {
                yield break;
            }
            catch (UnauthorizedAccessException)
            {
                yield break;
            }

            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (directory.Name.Contains("._del_", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (_options.SkipReparse && IsReparsePoint(directory))
                    continue;

                if (!CacheCleaner.IsShaderCacheDirectoryName(directory.Name))
                    continue;

                yield return directory;
            }
        }

        private static bool IsReparsePoint(DirectoryInfo directory)
        {
            try
            {
                return directory.Attributes.HasFlag(FileAttributes.ReparsePoint);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private Task<ShaderCacheEntry?> InspectDirectoryAsync(DirectoryInfo directory, CancellationToken cancellationToken)
        {
            return Task.Run(() => InspectDirectory(directory, cancellationToken), cancellationToken);
        }

        private ShaderCacheEntry? InspectDirectory(DirectoryInfo directory, CancellationToken cancellationToken)
        {
            try
            {
                long size = 0;
                DateTime lastWriteTime = directory.LastWriteTimeUtc;

                var stack = new Stack<DirectoryInfo>();
                stack.Push(directory);

                while (stack.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var current = stack.Pop();

                    FileInfo[] files;
                    try
                    {
                        files = current.GetFiles();
                    }
                    catch (IOException)
                    {
                        files = Array.Empty<FileInfo>();
                    }
                    catch (UnauthorizedAccessException)
                    {
                        files = Array.Empty<FileInfo>();
                    }

                    foreach (var file in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            size += file.Length;
                            if (file.LastWriteTimeUtc > lastWriteTime)
                                lastWriteTime = file.LastWriteTimeUtc;
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

                    DirectoryInfo[] subdirectories;
                    try
                    {
                        subdirectories = current.GetDirectories();
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }

                    foreach (var subDir in subdirectories)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (_options.SkipReparse && IsReparsePoint(subDir))
                            continue;

                        if (subDir.LastWriteTimeUtc > lastWriteTime)
                            lastWriteTime = subDir.LastWriteTimeUtc;

                        stack.Push(subDir);
                    }
                }

                return new ShaderCacheEntry(directory.Name, directory.FullName, size, lastWriteTime);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
