using StarCitizenUA.Interfaces;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StarCitizenUA.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private readonly ISearchFolder _searchFolder;
        private readonly ISettingsService _settingsService;

        private string? _gameFolder;

        public MainWindowViewModel(ISearchFolder searchFolder, ISettingsService settingsService)
        {
            _searchFolder = searchFolder;
            _settingsService = settingsService;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string? GameFolder
        {
            get => _gameFolder;
            private set
            {
                if (_gameFolder != value)
                {
                    _gameFolder = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsGameFolderSet));
                }
            }
        }

        public bool IsGameFolderSet => !string.IsNullOrWhiteSpace(GameFolder);

        public Task InitializeAsync()
        {
            GameFolder = _settingsService.GetGameFolder();
            return Task.CompletedTask;
        }

        public async Task<string?> DetectGameFolderAsync(int maxDepth, CancellationToken cancellationToken)
        {
            var found = await _searchFolder.FindGameFolderAsync(maxDepth, cancellationToken);
            if (string.IsNullOrEmpty(found))
            {
                return null;
            }

            if (_settingsService.TrySetGameFolder(found))
            {
                GameFolder = _settingsService.GetGameFolder();
                return GameFolder;
            }

            GameFolder = null;
            return null;
        }

        public bool TrySetGameFolder(string? path)
        {
            if (_settingsService.TrySetGameFolder(path))
            {
                GameFolder = _settingsService.GetGameFolder();
                return true;
            }

            return false;
        }

        public void ResetGameFolder()
        {
            _settingsService.ClearGameFolder();
            GameFolder = null;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
