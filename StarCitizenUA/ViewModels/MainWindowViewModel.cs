using StarCitizenUA.Interfaces;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace StarCitizenUA.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private readonly ISearchFolder _searchFolder;
        private readonly IVoiceAttackFolderHelper _voiceAttackFolderHelper;
        private readonly ISettingsService _settingsService;

        private string? _gameFolder;
        private string? _voiceAttackFolder;

        public MainWindowViewModel(ISearchFolder searchFolder, IVoiceAttackFolderHelper voiceAttackFolderHelper, ISettingsService settingsService)
        {
            _searchFolder = searchFolder;
            _voiceAttackFolderHelper = voiceAttackFolderHelper;
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

        public string? VoiceAttackFolder
        {
            get => _voiceAttackFolder;
            private set
            {
                if (_voiceAttackFolder != value)
                {
                    _voiceAttackFolder = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsVoiceAttackFolderSet));
                }
            }
        }

        public bool IsGameFolderSet => !string.IsNullOrWhiteSpace(GameFolder);
        public bool IsVoiceAttackFolderSet => !string.IsNullOrWhiteSpace(VoiceAttackFolder);

        public Task InitializeAsync()
        {
            GameFolder = _settingsService.GetGameFolder();
            VoiceAttackFolder = _settingsService.GetVoiceAttackFolder();
            return Task.CompletedTask;
        }

        public async Task<string?> DetectGameFolderAsync(int maxDepth, CancellationToken cancellationToken)
        {
            var found = await _searchFolder.FindGameFolderAsync(maxDepth, cancellationToken);
            if (!string.IsNullOrEmpty(found))
            {
                GameFolder = found;
            }

            return found;
        }

        public async Task<string?> DetectVoiceAttackFolderAsync(CancellationToken cancellationToken)
        {
            var found = await _voiceAttackFolderHelper.FindVoiceAttackImportFolderAsync(cancellationToken);
            if (!string.IsNullOrEmpty(found))
            {
                _settingsService.TrySetVoiceAttackFolder(found);
                VoiceAttackFolder = _settingsService.GetVoiceAttackFolder();
            }

            return found;
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

        public bool TrySetVoiceAttackFolder(string? path)
        {
            if (_settingsService.TrySetVoiceAttackFolder(path))
            {
                VoiceAttackFolder = _settingsService.GetVoiceAttackFolder();
                return true;
            }

            return false;
        }

        public void ResetGameFolder()
        {
            _settingsService.ClearGameFolder();
            GameFolder = null;
        }

        public void ResetVoiceAttackFolder()
        {
            _settingsService.ClearVoiceAttackFolder();
            VoiceAttackFolder = null;
        }

        public void CancelVoiceAttackSearch() => _voiceAttackFolderHelper.CancelActiveSearch();

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
