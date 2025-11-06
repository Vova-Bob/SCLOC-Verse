using StarCitizenUA.Application.Localization;
using StarCitizenUA.Helpers;
using StarCitizenUA.Infrastructure.Localization;
using StarCitizenUA.Interfaces;
using StarCitizenUA.Services;
using StarCitizenUA.Services.Common;
using StarCitizenUA.Services.LiaServices;
using StarCitizenUA.ViewModels;
using StarCitizenUA.Views;

namespace StarCitizenUA.Composition
{
    public class AppCompositionRoot
    {
        private readonly IIgnoreRulesProvider _ignoreRulesProvider;
        private readonly IFolderSearchService _folderSearchService;
        private readonly ISettingsService _settingsService;
        private readonly IUpdater _updater;
        private readonly UpdateCheckerService _updateCheckerService;

        public AppCompositionRoot()
        {
            _ignoreRulesProvider = new IgnoreRulesProvider();
            _folderSearchService = new FolderSearchService(_ignoreRulesProvider);
            _settingsService = new SettingsService();
            _updater = new Updater();
            _updateCheckerService = new UpdateCheckerService(new VoiceAttackFolderHelper(_folderSearchService, _settingsService));
        }

        public MainWindow CreateMainWindow()
        {
            var searchFolder = new SearchFolder(_folderSearchService, _settingsService);
            var voiceAttackHelper = new VoiceAttackFolderHelper(_folderSearchService, _settingsService);
            var viewModel = new MainWindowViewModel(searchFolder, voiceAttackHelper, _settingsService);

            var windowHelper = new WindowHelper();
            var metadataStore = new LocalizationMetadataStore();
            var gitHubClient = new GitHubLocalizationClient();
            var localizationInstaller = new LocalizationInstaller(gitHubClient, metadataStore);
            var readmeService = new ReadmeService();

            return new MainWindow(viewModel, windowHelper, localizationInstaller, readmeService, _updater, _updateCheckerService);
        }
    }
}
