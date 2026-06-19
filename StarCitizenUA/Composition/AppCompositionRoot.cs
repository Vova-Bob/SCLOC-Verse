using StarCitizenUA.Helpers;
using StarCitizenUA.Interfaces;
using StarCitizenUA.Services;
using StarCitizenUA.Services.ApplicationUpdate;
using StarCitizenUA.Services.Common;
using StarCitizenUA.Services.LiaServices;
using StarCitizenUA.Services.LocalizationServices;
using StarCitizenUA.ViewModels;
using System.Net.Http;


namespace StarCitizenUA.Composition
{
    public class AppCompositionRoot
    {
        private readonly IIgnoreRulesProvider _ignoreRulesProvider;
        private readonly IFolderSearchService _folderSearchService;
        private readonly ISettingsService _settingsService;
        private readonly IUpdater _updater;
        private readonly UpdateCheckerService _updateCheckerService;
        private readonly IApplicationVersionProvider _applicationVersionProvider;
        private readonly IUpdateChannelService _updateChannelService;
        private readonly IApplicationUpdateService _applicationUpdateService;
        private readonly IUpdateDownloader _updateDownloader;
        private readonly IUpdateInstaller _updateInstaller;
        private readonly IUpdateHistoryService _updateHistoryService;
        private readonly IUpdateVerifier _updateVerifier;

        public AppCompositionRoot()
        {
            _ignoreRulesProvider = new IgnoreRulesProvider();
            _folderSearchService = new FolderSearchService(_ignoreRulesProvider);
            _settingsService = new SettingsService();
            _updater = new Updater();
            _updateCheckerService = new UpdateCheckerService(_updater);

            _applicationVersionProvider = new ApplicationVersionProvider();
            _updateChannelService = (IUpdateChannelService)_settingsService;

            var httpClient = new HttpClient();
            var gitHubClient = new GitHubReleaseClient(httpClient, UpdateConstants.UserAgent);
            var updateCacheService = new UpdateCacheService();
            var releaseChannelResolver = new ReleaseChannelResolver();

            _applicationUpdateService = new ApplicationUpdateService(
                "Vova-Bob",
                "SCLocalizationUA",
                _applicationVersionProvider,
                _updateChannelService,
                gitHubClient,
                updateCacheService,
                releaseChannelResolver);

            _updateDownloader = new UpdateDownloader(httpClient);
            _updateInstaller = new UpdateInstaller(new UpdateScriptBuilder());
            _updateHistoryService = new UpdateHistoryService();
            _updateVerifier = new UpdateVerifier();
        }

        public MainWindow CreateMainWindow()
        {
            var searchFolder = new SearchFolder(_folderSearchService, _settingsService);
            var viewModel = new MainWindowViewModel(searchFolder, _settingsService);

            var windowHelper = new WindowHelper();
            var localizationInstaller = new LocalizationInstaller();
            var readmeService = new ReadmeService();

            return new MainWindow(
                viewModel,
                windowHelper,
                localizationInstaller,
                readmeService,
                _updater,
                _updateCheckerService,
                _applicationUpdateService,
                _updateChannelService,
                _applicationVersionProvider,
                _updateDownloader,
                _updateInstaller,
                _updateHistoryService,
                _updateVerifier);
        }
    }
}
