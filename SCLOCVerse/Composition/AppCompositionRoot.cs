using SCLOCVerse.Helpers;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Services;
using SCLOCVerse.Services.ApplicationUpdate;
using SCLOCVerse.Services.Common;
using SCLOCVerse.Services.LiaServices;
using SCLOCVerse.Services.LocalizationServices;
using SCLOCVerse.ViewModels;
using System.Net.Http;
using System.Windows.Threading;


namespace SCLOCVerse.Composition
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
        private readonly IBackgroundUpdateMonitor _backgroundUpdateMonitor;
        private readonly IUpdateDownloader _updateDownloader;
        private readonly IUpdateInstaller _updateInstaller;
        private readonly IUpdateHistoryService _updateHistoryService;
        private readonly IUpdateVerifier _updateVerifier;
        private readonly IGitHubReleaseClient _gitHubReleaseClient;
        private readonly IDialogService _dialogService;
        private readonly AuthCompositionRoot _authCompositionRoot;

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

            _applicationUpdateService = new ApplicationUpdateService(
                "Vova-Bob",
                "SCLOC-Verse",
                _applicationVersionProvider,
                _updateChannelService,
                gitHubClient,
                updateCacheService);

            _backgroundUpdateMonitor = new BackgroundUpdateMonitor(_applicationUpdateService);

            _updateDownloader = new UpdateDownloader(httpClient);
            _updateInstaller = new UpdateInstaller(new UpdateScriptBuilder());
            _updateHistoryService = new UpdateHistoryService();
            _updateVerifier = new UpdateVerifier();
            _gitHubReleaseClient = gitHubClient;
            _dialogService = new DialogService(Dispatcher.CurrentDispatcher);

            var supabaseUrl = GetSupabaseUrl();
            var supabaseAnonKey = GetSupabaseAnonKey();
            _authCompositionRoot = new AuthCompositionRoot(supabaseUrl, supabaseAnonKey);
        }

        public AuthCompositionRoot AuthCompositionRoot => _authCompositionRoot;

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
                _backgroundUpdateMonitor,
                _updateChannelService,
                _applicationVersionProvider,
                _updateDownloader,
                _updateInstaller,
                _updateHistoryService,
                _updateVerifier,
                _gitHubReleaseClient,
                _dialogService,
                _authCompositionRoot.AuthService,
                _authCompositionRoot.AuthStatusProvider);
        }

        private static string GetSupabaseUrl()
        {
            var value = System.Environment.GetEnvironmentVariable("SCLOCVERSE_SUPABASE_URL");
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            value = SCLOCVerse.Properties.SupabaseConfig.DefaultUrl;
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            return "https://placeholder.supabase.co";
        }

        private static string GetSupabaseAnonKey()
        {
            var value = System.Environment.GetEnvironmentVariable("SCLOCVERSE_SUPABASE_ANON_KEY");
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            value = SCLOCVerse.Properties.SupabaseConfig.DefaultAnonKey;
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            return "placeholder-anon-key";
        }
    }
}
