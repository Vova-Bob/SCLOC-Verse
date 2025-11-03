using StarCitizenUA.Helpers;
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

        public AppCompositionRoot()
        {
            _ignoreRulesProvider = new IgnoreRulesProvider();
            _folderSearchService = new FolderSearchService(_ignoreRulesProvider);
            _settingsService = new SettingsService();
        }

        public MainWindow CreateMainWindow()
        {
            var searchFolder = new SearchFolder(_folderSearchService, _settingsService);
            var voiceAttackHelper = new VoiceAttackFolderHelper(_folderSearchService, _settingsService);
            var viewModel = new MainWindowViewModel(searchFolder, voiceAttackHelper, _settingsService);

            var windowHelper = new WindowHelper();
            var localizationInstaller = new LocalizationInstaller();
            var readmeService = new ReadmeService();

            return new MainWindow(viewModel, windowHelper, localizationInstaller, readmeService);
        }
    }
}
