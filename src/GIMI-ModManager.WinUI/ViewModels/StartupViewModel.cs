using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.Core.Services;
using GIMI_ModManager.Core.Services.CommandService;
using GIMI_ModManager.Core.Services.GameBanana;
using GIMI_ModManager.Core.Services.ModPresetService;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Contracts.ViewModels;
using GIMI_ModManager.WinUI.Models.Options;
using GIMI_ModManager.WinUI.Models.Settings;
using GIMI_ModManager.WinUI.Services;
using GIMI_ModManager.WinUI.Services.AppManagement;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.Validators.PreConfigured;
using GIMI_ModManager.WinUI.ViewModels.SubVms;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace GIMI_ModManager.WinUI.ViewModels;

public partial class StartupViewModel : ObservableRecipient, INavigationAware
{
    private readonly INavigationService _navigationService;
    private readonly ILogger _logger = Log.ForContext<StartupViewModel>();
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IWindowManagerService _windowManagerService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly IGameService _gameService;
    private readonly ModPresetService _modPresetService;
    private readonly UserPreferencesService _userPreferencesService;
    private readonly SelectedGameService _selectedGameService;
    private readonly ModArchiveRepository _modArchiveRepository;
    private readonly CommandService _commandService;
    private readonly ICommunityGamesService _communityGamesService;
    private readonly ILanguageLocalizer _localizer;
    private readonly LifeCycleService _lifeCycleService;
    private readonly Dictionary<string, string> _nameToLangCode = new();

    public PathPicker PathToGIMIFolderPicker { get; }

    public PathPicker PathToModsFolderPicker { get; }
    [ObservableProperty] private bool _reorganizeModsOnStartup;
    [ObservableProperty] private bool _disableMods;

    [ObservableProperty]
    private GameComboBoxEntryVM _selectedGame = new GameComboBoxEntryVM(SupportedGames.Genshin)
    {
        GameName = "Genshin Impact",
        GameShortName = SupportedGames.Genshin.ToString(),
        GameIconPath = null!
    };

    [ObservableProperty] private string _modelImporterName = "Genshin-Impact-Model-Importer";
    [ObservableProperty] private string _modelImporterShortName = "GIMI";
    [ObservableProperty] private Uri _gameBananaUrl = new("https://gamebanana.com/games/8552");

    [ObservableProperty] private Uri _modelImporterUrl = new("https://github.com/SilentNightSound");

    public ObservableCollection<GameComboBoxEntryVM> Games { get; } = new();

    [ObservableProperty] private ObservableCollection<string> _languages = new();
    [ObservableProperty] private string _selectedLanguage = string.Empty;

    public StartupViewModel(INavigationService navigationService, ILocalSettingsService localSettingsService,
        IWindowManagerService windowManagerService, ISkinManagerService skinManagerService,
        SelectedGameService selectedGameService, IGameService gameService, ModPresetService modPresetService,
        UserPreferencesService userPreferencesService, ModArchiveRepository modArchiveRepository,
        CommandService commandService, ICommunityGamesService communityGamesService,
        ILanguageLocalizer localizer, LifeCycleService lifeCycleService)
    {
        _navigationService = navigationService;
        _localSettingsService = localSettingsService;
        _windowManagerService = windowManagerService;
        _skinManagerService = skinManagerService;
        _selectedGameService = selectedGameService;
        _gameService = gameService;
        _modPresetService = modPresetService;
        _userPreferencesService = userPreferencesService;
        _modArchiveRepository = modArchiveRepository;
        _commandService = commandService;
        _communityGamesService = communityGamesService;
        _localizer = localizer;
        _lifeCycleService = lifeCycleService;

        PathToGIMIFolderPicker = new PathPicker([]);

        PathToModsFolderPicker =
            new PathPicker(ModsFolderValidator.Validators);

        PathToGIMIFolderPicker.IsValidChanged += (sender, args) => SaveStartupSettingsCommand.NotifyCanExecuteChanged();
        PathToModsFolderPicker.IsValidChanged +=
            (sender, args) => SaveStartupSettingsCommand.NotifyCanExecuteChanged();
    }


    private bool ValidStartupSettings() => PathToGIMIFolderPicker.IsValid && PathToModsFolderPicker.IsValid &&
                                           PathToGIMIFolderPicker.Path != PathToModsFolderPicker.Path;


    [RelayCommand(CanExecute = nameof(ValidStartupSettings))]
    private async Task SaveStartupSettings()
    {
        var modManagerOptions = new ModManagerOptions()
        {
            GimiRootFolderPath = PathToGIMIFolderPicker.Path,
            ModsFolderPath = PathToModsFolderPicker.Path,
            UnloadedModsFolderPath = null
        };

        var selectedGameStr = SelectedGame.Value.ToString();
        await _selectedGameService.SetSelectedGame(selectedGameStr);

        var gameDir = Path.Combine(App.ASSET_DIR, "Games", selectedGameStr);
        if (modManagerOptions.GameSource == GameSource.Community)
        {
            var communityDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JASM", "CommunityGames");
            if (_communityGamesService.VerifyIntegrity(communityDir, new[] { selectedGameStr }))
            {
                gameDir = Path.Combine(communityDir, "Games", selectedGameStr);
                if (!Directory.Exists(gameDir))
                    gameDir = Path.Combine(communityDir, selectedGameStr);
            }
        }

        await _gameService.InitializeAsync(
            gameDir,
            _localSettingsService.ApplicationDataFolder);

        await _localSettingsService.SaveSettingAsync(ModManagerOptions.Section,
            modManagerOptions);
        _logger.Information("Saved startup settings: {@ModManagerOptions}", modManagerOptions);

        await _skinManagerService.InitializeAsync(modManagerOptions.ModsFolderPath!, null,
            modManagerOptions.GimiRootFolderPath);

        var modArchiveSettings =
            await _localSettingsService.ReadOrCreateSettingAsync<ModArchiveSettings>(ModArchiveSettings.Key);

        var tasks = new List<Task>
        {
            _userPreferencesService.InitializeAsync(),
            _modPresetService.InitializeAsync(_localSettingsService.ApplicationDataFolder),
            _modArchiveRepository.InitializeAsync(_localSettingsService.ApplicationDataFolder,
                o => o.MaxDirectorySizeGb = modArchiveSettings.MaxLocalArchiveCacheSizeGb),
            _commandService.InitializeAsync(_localSettingsService.ApplicationDataFolder)
        };

        await Task.WhenAll(tasks);

        await _localSettingsService.SaveSettingAsync(ActivationService.IgnoreNewFolderStructureKey, true);

        if (ReorganizeModsOnStartup)
        {
            await Task.Run(() => _skinManagerService.ReorganizeModsAsync(disableMods: DisableMods));
        }


        _navigationService.NavigateTo(typeof(CharactersViewModel).FullName!, null, true);
        _windowManagerService.ResizeWindowPercent(_windowManagerService.MainWindow, 80, 80);
        _windowManagerService.MainWindow.CenterOnScreen();
        var localizer = App.GetService<ILanguageLocalizer>();
        App.GetService<NotificationManager>().ShowNotification(localizer.GetLocalizedStringOrDefault("Settings_Startup_SettingsSavedTitle") ?? "Startup settings saved",
            string.Format(localizer.GetLocalizedStringOrDefault("Settings_Startup_SettingsSavedMessage") ?? "Startup settings saved successfully to '{0}'", _localSettingsService.GameScopedSettingsLocation),
            TimeSpan.FromSeconds(7));
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        Task.Run(async () =>
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        {
            await Task.Delay(TimeSpan.FromSeconds(7));
            App.GetService<NotificationManager>().ShowNotification(localizer.GetLocalizedStringOrDefault("Settings_Startup_AlphaTitle") ?? "JASM is still in alpha",
                localizer.GetLocalizedStringOrDefault("Settings_Startup_AlphaMessage") ?? "There will be bugs and things will most likely break. Anyway, hope you enjoy using JASM+!",
                TimeSpan.FromSeconds(20));
        });
    }


    [RelayCommand]
    private async Task BrowseGimiModFolderAsync()
    {
        await PathToGIMIFolderPicker.BrowseFolderPathAsync(App.MainWindow);
        if (PathToGIMIFolderPicker.PathHasValue &&
            !PathToModsFolderPicker.PathHasValue)
            PathToModsFolderPicker.Path = Path.Combine(PathToGIMIFolderPicker.Path!, "Mods");
    }


    [RelayCommand]
    private async Task BrowseModsFolderAsync()
        => await PathToModsFolderPicker.BrowseFolderPathAsync(App.MainWindow);

    [RelayCommand]
    private async Task SelectLanguage(string? selectedLanguageName)
    {
        if (selectedLanguageName is null || !_nameToLangCode.TryGetValue(selectedLanguageName, out var langCode))
            return;

        if (langCode == _localizer.CurrentLanguage.LanguageCode)
            return;

        var localizerText = App.GetService<ILanguageLocalizer>();
        var restartDialog = new ContentDialog()
        {
            Title = localizerText.GetLocalizedStringOrDefault("Settings_RestartRequired_LangTitle") ?? "Restart Required",
            Content = new TextBlock()
            {
                Text = localizerText.GetLocalizedStringOrDefault("/Settings/ChangeLanguageDialogText",
                    defaultValue:
                    "Changing the language requires a restart of the application.\n" +
                    "This is required to ensure that the application is configured correctly for the selected language.\n\n" +
                    "Do you want to change the language?"),
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true
            },
            PrimaryButtonText = localizerText.GetLocalizedStringOrDefault("Settings_RestartRequired_LangPrimaryButton") ?? "Change Language and restart",
            CloseButtonText = localizerText.GetLocalizedStringOrDefault("Settings_RestartRequired_LangCloseButton") ?? "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await _windowManagerService.ShowDialogAsync(restartDialog);

        var currentLanguage = _localizer.CurrentLanguage.LanguageName;
        if (result != ContentDialogResult.Primary)
        {
            SelectedLanguage = currentLanguage;
            return;
        }

        await _localizer.SetLanguageAsync(langCode);

        var appSettings = await _localSettingsService.ReadOrCreateSettingAsync<AppSettings>(AppSettings.Key);
        appSettings.Language = langCode;
        await _localSettingsService.SaveSettingAsync(AppSettings.Key, appSettings);
        currentLanguage = _localizer.CurrentLanguage.LanguageName;
        SelectedLanguage = currentLanguage;

        await _lifeCycleService.RestartAsync(notifyOnError: true);
    }

    private void SetLanguageOptions()
    {
        Languages.Clear();
        _nameToLangCode.Clear();

        var supportedCultures = _localizer.AvailableLanguages.Select(l => l.LanguageCode).ToArray();
        foreach (var language in _localizer.AvailableLanguages)
        {
            var languageName = language.LanguageName;
            Languages.Add(languageName);
            _nameToLangCode[languageName] = language.LanguageCode;
        }

        _ = supportedCultures;
        SelectedLanguage = _localizer.CurrentLanguage.LanguageName;
    }

    public async void OnNavigatedTo(object parameter)
    {
        _windowManagerService.ResizeWindowPercent(_windowManagerService.MainWindow, 50, 60);
        _windowManagerService.MainWindow.CenterOnScreen();

        var settings =
            await _localSettingsService.ReadOrCreateSettingAsync<ModManagerOptions>(ModManagerOptions.Section);

        SetLanguageOptions();

        await SetGameComboBoxValues();

        SetSelectedGame(await _selectedGameService.GetSelectedGameAsync());
        await SetGameInfo(SelectedGame.Value.ToString());
        SetPaths(settings);
        ReorganizeModsOnStartup = true;
    }

    [RelayCommand]
    private async Task SetGameAsync(string game)
    {
        if (game.IsNullOrEmpty())
            return;

        await _selectedGameService.SetSelectedGame(game);
        SetSelectedGame(game);

        var settings =
            await _localSettingsService.ReadOrCreateSettingAsync<ModManagerOptions>(ModManagerOptions.Section);

        await SetGameInfo(game);
        SetPaths(settings);
    }


    private async Task SetGameInfo(string game)
    {
        var gameInfo = await GameService.GetGameInfoAsync(Enum.Parse<SupportedGames>(game));

        if (gameInfo is null)
        {
            _logger.Error("Game info for {Game} is null", game);
            return;
        }

        ModelImporterName = gameInfo.GameModelImporterName;
        ModelImporterShortName = gameInfo.GameModelImporterShortName;
        GameBananaUrl = gameInfo.GameBananaUrl;
        ModelImporterUrl = gameInfo.GameModelImporterUrl;
        var folderWarning = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("Settings_FolderWarning_No3DMigotoEntry") ?? "Folder does not contain any entry with the specified names:";
        PathToGIMIFolderPicker.SetValidators(GimiFolderRootValidators.Validators(gameInfo.GameModelImporterExeNames, folderWarning));
    }

    private async Task SetGameComboBoxValues()
    {
        foreach (var supportedGame in Enum.GetValues<SupportedGames>())
        {
            var gameInfo = await GameService.GetGameInfoAsync(supportedGame);
            if (gameInfo is null)
                continue;

            Games.Add(new GameComboBoxEntryVM(supportedGame)
            {
                GameIconPath = new Uri(gameInfo.GameIcon),
                GameName = gameInfo.GameName,
                GameShortName = gameInfo.GameShortName
            });
        }
    }

    private void SetSelectedGame(string game)
    {
        var selectedGame = Games.FirstOrDefault(g => g.Value.ToString() == game);
        if (selectedGame is not null)
            SelectedGame = selectedGame;
    }


    private void SetPaths(ModManagerOptions settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.GimiRootFolderPath))
            PathToGIMIFolderPicker.Path = settings.GimiRootFolderPath;
        else
            PathToGIMIFolderPicker.Path = "";

        if (!string.IsNullOrWhiteSpace(settings.ModsFolderPath))
            PathToModsFolderPicker.Path = settings.ModsFolderPath;
        else
            PathToModsFolderPicker.Path = "";
    }


    public void OnNavigatedFrom()
    {
    }
}