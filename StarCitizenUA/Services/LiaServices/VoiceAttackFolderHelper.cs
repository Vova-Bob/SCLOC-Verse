using StarCitizenUA.Interfaces;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;

namespace StarCitizenUA.Services.LiaServices
{
    class VoiceAttackFolderHelper : IVoiceAttackFolderHelper
    {
        private readonly IFolderSearchService _folderSearchService;
        private readonly ISettingsService _settingsService;
        private readonly SemaphoreSlim _throttle = new(4);
        private CancellationTokenSource? _searchCts;
        private int _activeEnumerations;

        public VoiceAttackFolderHelper(IFolderSearchService folderSearchService, ISettingsService settingsService)
        {
            _folderSearchService = folderSearchService;
            _settingsService = settingsService;
        }

        public void CancelActiveSearch()
        {
            _searchCts?.Cancel();
        }

        public async Task<string?> FindVoiceAttackImportFolderAsync(CancellationToken cancellationToken)
        {
            var knownLocations = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VoiceAttack 2", "Apps", "Import"),
                @"C:\\Program Files (x86)\\VoiceAttack\\Apps\\Import",
                @"C:\\Program Files\\VoiceAttack\\Apps\\Import",
                @"C:\\Program Files (x86)\\SteamLibrary\\steamapps\\common\\VoiceAttack 2\\Apps\\Import"
            };

            foreach (var location in knownLocations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Directory.Exists(location))
                    return location;
            }

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _searchCts = linkedCts;
            var token = linkedCts.Token;

            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToArray();
            var queue = new ConcurrentQueue<(string Path, int Depth)>();
            foreach (var drive in drives)
            {
                queue.Enqueue((drive.RootDirectory.FullName, 0));
            }

            var workers = Enumerable.Range(0, Environment.ProcessorCount)
                .Select(_ => Task.Run(() => SearchWorkerAsync(queue, token), token))
                .ToArray();

            try
            {
                var completed = await Task.WhenAny(workers).ConfigureAwait(false);
                var result = await completed.ConfigureAwait(false);

                if (!string.IsNullOrEmpty(result))
                {
                    linkedCts.Cancel();
                    var importPath = Path.Combine(result, "Apps", "Import");
                    return Directory.Exists(importPath) ? importPath : null;
                }

                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[VoiceAttackFolderHelper] Пошук перервано.");
            }
            finally
            {
                linkedCts.Dispose();
                if (ReferenceEquals(_searchCts, linkedCts))
                {
                    _searchCts = null;
                }
            }

            return null;
        }

        private async Task<string?> SearchWorkerAsync(ConcurrentQueue<(string Path, int Depth)> queue, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (!queue.TryDequeue(out var current))
                {
                    if (Volatile.Read(ref _activeEnumerations) == 0 && queue.IsEmpty)
                    {
                        return null;
                    }

                    await Task.Delay(50, token).ConfigureAwait(false);
                    continue;
                }

                if (current.Depth > 5)
                    continue;

                string[] subdirs;
                await _throttle.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    Interlocked.Increment(ref _activeEnumerations);
                    subdirs = _folderSearchService
                        .EnumerateAccessibleDirectories(current.Path)
                        .ToArray();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[VoiceAttackFolderHelper] Помилка читання каталогу {current.Path}: {ex.Message}");
                    subdirs = Array.Empty<string>();
                }
                finally
                {
                    Interlocked.Decrement(ref _activeEnumerations);
                    _throttle.Release();
                }

                foreach (var dir in subdirs)
                {
                    if (token.IsCancellationRequested)
                        return null;

                    var dirName = Path.GetFileName(dir);
                    if (dirName.Equals("VoiceAttack 2", StringComparison.OrdinalIgnoreCase))
                    {
                        var importPath = Path.Combine(dir, "Apps", "Import");
                        if (Directory.Exists(importPath))
                        {
                            return dir;
                        }
                    }

                    queue.Enqueue((dir, current.Depth + 1));
                }
            }

            return null;
        }

        public async Task<string?> GetOrPromptVoiceAttackFolderAsync(TextBox folderDisplayControl, CancellationToken cancellationToken)
        {
            var savedFolder = _settingsService.GetVoiceAttackFolder();
            if (!string.IsNullOrEmpty(savedFolder))
            {
                folderDisplayControl.Text = savedFolder;
                return savedFolder;
            }

            var foundFolder = await FindVoiceAttackImportFolderAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(foundFolder))
            {
                if (_settingsService.TrySetVoiceAttackFolder(foundFolder))
                {
                    folderDisplayControl.Text = foundFolder;
                    return foundFolder;
                }
            }

            return null;
        }
    }
}
