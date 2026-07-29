using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using Windows.Storage.Pickers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using GIMI_ModManager.Core.Contracts.Entities;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.GamesService.Interfaces;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.Core.Services;
using GIMI_ModManager.Core.Services.GameBanana;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Contracts.ViewModels;
using GIMI_ModManager.WinUI.Helpers;
using GIMI_ModManager.WinUI.Models.Options;
using GIMI_ModManager.WinUI.Models.Settings;
using GIMI_ModManager.WinUI.Services;
using GIMI_ModManager.WinUI.Services.AppManagement;
using GIMI_ModManager.WinUI.Services.AppManagement.Updating;
using GIMI_ModManager.WinUI.Services.ModHandling;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.Validators.PreConfigured;
using GIMI_ModManager.WinUI.ViewModels.SettingsViewModels;
using GIMI_ModManager.WinUI.ViewModels.SubVms;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Serilog;

namespace GIMI_ModManager.WinUI.ViewModels;

public partial class SettingsViewModel : ObservableRecipient, INavigationAware
{
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ILogger _logger;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly INavigationViewService _navigationViewService;
    private readonly IWindowManagerService _windowManagerService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly IGameService _gameService;
    private readonly ILanguageLocalizer _localizer;
    private readonly SelectedGameService _selectedGameService;
    private readonly ModUpdateAvailableChecker _modUpdateAvailableChecker;
    private readonly LifeCycleService _lifeCycleService;
    private readonly INavigationService _navigationService;
    private readonly ModArchiveRepository _modArchiveRepository;


    private readonly NotificationManager _notificationManager;
    private readonly UpdateChecker _updateChecker;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetGenshinExePathCommand))]
    public GenshinProcessManager _genshinProcessManager;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(Reset3DmigotoPathCommand))]
    public ThreeDMigtoProcessManager _threeDMigtoProcessManager;


    [ObservableProperty] private ElementTheme _elementTheme;

    [ObservableProperty] private string _versionDescription;

    [ObservableProperty] private string _latestVersion = string.Empty;
    [ObservableProperty] private bool _showNewVersionAvailable = false;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(IgnoreNewVersionCommand))]
    private bool _CanIgnoreUpdate = false;

    [ObservableProperty] private ObservableCollection<string> _languages = new();
    [ObservableProperty] private string _selectedLanguage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _games = new()
    {
        SupportedGames.Genshin.ToString(),
        SupportedGames.Honkai.ToString(),
        SupportedGames.WuWa.ToString(),
        SupportedGames.ZZZ.ToString(),
        SupportedGames.Endfield.ToString()
    };

    [ObservableProperty] private string _selectedGame = string.Empty;

    [ObservableProperty] private string _modCheckerStatus = ModUpdateAvailableChecker.RunningState.Waiting.ToString();

    [ObservableProperty] private bool _isModUpdateCheckerEnabled = false;

    [ObservableProperty] private DateTime? _nextModCheckTime = null;

    [ObservableProperty] private bool _characterAsSkinsCheckbox = false;

    [ObservableProperty] private int _maxCacheLimit;

    [ObservableProperty] private Uri _archiveCacheFolderPath;

    [ObservableProperty] private bool _persistWindowSize = false;

    [ObservableProperty] private bool _persistWindowPosition = false;

    private Dictionary<string, string> _nameToLangCode = new();

    public PathPicker PathToGIMIFolderPicker { get; }
    public PathPicker PathToModsFolderPicker { get; }

    [ObservableProperty] private bool _legacyCharacterDetails;


    private ModManagerOptions? _modManagerOptions = null!;

    [ObservableProperty] private string _modCacheSizeGB = string.Empty;

    private readonly ICommunityGamesService _communityGamesService;

    [ObservableProperty] private GameSource _selectedGameSource = GameSource.Release;
    [ObservableProperty] private string _communityRepoUrl = string.Empty;
    [ObservableProperty] private bool _isCommunitySourceSelected;

    partial void OnSelectedGameSourceChanged(GameSource value)
    {
        IsCommunitySourceSelected = value == GameSource.Community;
        _ = SaveGameSourceSettingsAsync();
    }

    partial void OnCommunityRepoUrlChanged(string value)
    {
        _ = SaveGameSourceSettingsAsync();
    }

    private async Task SaveGameSourceSettingsAsync()
    {
        var modManagerOptions = await _localSettingsService.ReadOrCreateSettingAsync<ModManagerOptions>(ModManagerOptions.Section);
        if (modManagerOptions.GameSource == SelectedGameSource && modManagerOptions.CommunityRepoUrl == CommunityRepoUrl)
            return;

        modManagerOptions.GameSource = SelectedGameSource;
        modManagerOptions.CommunityRepoUrl = CommunityRepoUrl;
        await _localSettingsService.SaveSettingAsync(ModManagerOptions.Section, modManagerOptions);

        var restartDialog = new ContentDialog()
        {
            Title = _localizer.GetLocalizedStringOrDefault("Settings_GameSource_RestartTitle") ?? "Restart Required",
            Content = new TextBlock()
            {
                Text = _localizer.GetLocalizedStringOrDefault("Settings_GameSource_RestartContent") ?? "Changing the Game Source requires a restart of the application to load the new games. JASM will close now.",
                TextWrapping = TextWrapping.WrapWholeWords
            },
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Settings_GameSource_RestartPrimary") ?? "Restart now",
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Settings_GameSource_RestartClose") ?? "Restart later",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await _windowManagerService.ShowDialogAsync(restartDialog);
        if (result == ContentDialogResult.Primary)
        {
            await RestartAppAsync(0);
        }
    }

    [RelayCommand]
    private async Task UpdateCommunityGamesAsync()
    {
        if (string.IsNullOrWhiteSpace(CommunityRepoUrl))
        {
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Settings_Notification_ErrorTitle") ?? "Error", _localizer.GetLocalizedStringOrDefault("Settings_CommunityGames_EmptyUrl") ?? "Community Repo URL cannot be empty.", TimeSpan.FromSeconds(3));
            return;
        }

        var communityDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JASM", "CommunityGames");
        _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Settings_Notification_UpdatingTitle") ?? "Updating...", _localizer.GetLocalizedStringOrDefault("Settings_CommunityGames_Pulling") ?? "Pulling latest community games. This might take a moment.", null);

        try
        {
            var success = await _communityGamesService.TryUpdateCommunityGamesAsync(CommunityRepoUrl, communityDir);

            if (success)
            {
                var games = new[] { await _selectedGameService.GetSelectedGameAsync() };
                if (_communityGamesService.VerifyIntegrity(communityDir, games))
                {
                    _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Settings_Notification_SuccessTitle") ?? "Success", _localizer.GetLocalizedStringOrDefault("Settings_CommunityGames_Updated") ?? "Community games updated and verified successfully.", TimeSpan.FromSeconds(5));
                    await RestartAppAsync(2);
                }
                else
                {
                    _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Settings_Notification_ErrorTitle") ?? "Error", _localizer.GetLocalizedStringOrDefault("Settings_CommunityGames_IntegrityFailed") ?? "Integrity check failed. Check if repo matches expected structure.", TimeSpan.FromSeconds(5));
                }
            }
            else
            {
                _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Settings_Notification_ErrorTitle") ?? "Error", _localizer.GetLocalizedStringOrDefault("Settings_CommunityGames_UpdateFailed") ?? "Failed to update community games.", TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update community games");
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Settings_Notification_ErrorTitle") ?? "Error", _localizer.GetLocalizedStringOrDefault("Settings_CommunityGames_ExceptionPulling") ?? "Exception occurred while pulling repo. Check logs.", TimeSpan.FromSeconds(5));
        }
    }

    public SettingsViewModel(
        IThemeSelectorService themeSelectorService, ILocalSettingsService localSettingsService,
        ILogger logger, NotificationManager notificationManager,
        INavigationViewService navigationViewService, IWindowManagerService windowManagerService,
        ISkinManagerService skinManagerService, UpdateChecker updateChecker,
        GenshinProcessManager genshinProcessManager, ThreeDMigtoProcessManager threeDMigtoProcessManager,
        IGameService gameService, ILanguageLocalizer localizer,
        SelectedGameService selectedGameService, ModUpdateAvailableChecker modUpdateAvailableChecker,
        LifeCycleService lifeCycleService, INavigationService navigationService,
        ModArchiveRepository modArchiveRepository, ICommunityGamesService communityGamesService)
    {
        _themeSelectorService = themeSelectorService;
        _localSettingsService = localSettingsService;
        _notificationManager = notificationManager;
        _navigationViewService = navigationViewService;
        _windowManagerService = windowManagerService;
        _skinManagerService = skinManagerService;
        _updateChecker = updateChecker;
        _gameService = gameService;

        _localizer = localizer;
        _selectedGameService = selectedGameService;
        _modUpdateAvailableChecker = modUpdateAvailableChecker;
        _lifeCycleService = lifeCycleService;
        _navigationService = navigationService;
        _modArchiveRepository = modArchiveRepository;
        _communityGamesService = communityGamesService;
        GenshinProcessManager = genshinProcessManager;
        ThreeDMigtoProcessManager = threeDMigtoProcessManager;
        _logger = logger.ForContext<SettingsViewModel>();
        _elementTheme = _themeSelectorService.Theme;
        _versionDescription = GetVersionDescription();

        _updateChecker.NewVersionAvailable += UpdateCheckerOnNewVersionAvailable;

        if (_updateChecker.LatestRetrievedVersion is not null &&
            _updateChecker.LatestRetrievedVersion != _updateChecker.CurrentVersion)
        {
            LatestVersion = VersionFormatter(_updateChecker.LatestRetrievedVersion);
            ShowNewVersionAvailable = true;
            if (_updateChecker.LatestRetrievedVersion != _updateChecker.IgnoredVersion)
                CanIgnoreUpdate = true;
        }

        ArchiveCacheFolderPath = _modArchiveRepository.ArchiveDirectory;

        _modManagerOptions = localSettingsService.ReadSetting<ModManagerOptions>(ModManagerOptions.Section);
        PathToGIMIFolderPicker = new PathPicker();
        PathToModsFolderPicker = new PathPicker(ModsFolderValidator.Validators);

        CharacterAsSkinsCheckbox = _modManagerOptions?.CharacterSkinsAsCharacters ?? false;

        PathToGIMIFolderPicker.Path = _modManagerOptions?.GimiRootFolderPath;
        PathToModsFolderPicker.Path = _modManagerOptions?.ModsFolderPath;

        _selectedGameSource = _modManagerOptions?.GameSource ?? GameSource.Release;
        _communityRepoUrl = _modManagerOptions?.CommunityRepoUrl ?? "https://github.com/zurce/JASM-Community-Resources";
        _isCommunitySourceSelected = _selectedGameSource == GameSource.Community;


        PathToGIMIFolderPicker.IsValidChanged += (sender, args) => SaveSettingsCommand.NotifyCanExecuteChanged();
        PathToModsFolderPicker.IsValidChanged +=
            (sender, args) => SaveSettingsCommand.NotifyCanExecuteChanged();


        PathToGIMIFolderPicker.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(PathPicker.Path))
                SaveSettingsCommand.NotifyCanExecuteChanged();
        };

        PathToModsFolderPicker.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(PathPicker.Path))
                SaveSettingsCommand.NotifyCanExecuteChanged();
        };
        MaxCacheLimit = localSettingsService.ReadSetting<ModArchiveSettings>(ModArchiveSettings.Key)
            ?.MaxLocalArchiveCacheSizeGb ?? new ModArchiveSettings().MaxLocalArchiveCacheSizeGb;
        SetCacheString(MaxCacheLimit);

        var cultures = CultureInfo.GetCultures(CultureTypes.AllCultures);
        cultures = cultures.Append(new CultureInfo("zh-cn")).ToArray();


        var supportedCultures = _localizer.AvailableLanguages.Select(l => l.LanguageCode).ToArray();

        foreach (var culture in cultures)
        {
            if (!supportedCultures.Contains(culture.Name.ToLower())) continue;

            Languages.Add(culture.NativeName);
            _nameToLangCode.Add(culture.NativeName, culture.Name.ToLower());

            if (_localizer.CurrentLanguage.Equals(culture))
                SelectedLanguage = culture.NativeName;
        }

        ModCheckerStatus = _localizer.GetLocalizedStringOrDefault(_modUpdateAvailableChecker.Status.ToString(),
            _modUpdateAvailableChecker.Status.ToString());
        NextModCheckTime = _modUpdateAvailableChecker.NextRunAt;
        _modUpdateAvailableChecker.OnUpdateCheckerEvent += (sender, args) =>
        {
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                ModCheckerStatus = _localizer.GetLocalizedStringOrDefault(_modUpdateAvailableChecker.Status.ToString(),
                    _modUpdateAvailableChecker.Status.ToString());
                NextModCheckTime = args.NextRunAt;
            });
        };
    }


    [RelayCommand]
    private async Task SwitchThemeAsync(ElementTheme param)
    {
        if (ElementTheme != param)
        {
            var result = await _windowManagerService.ShowDialogAsync(new ContentDialog()
            {
                Title = _localizer.GetLocalizedStringOrDefault("Settings_RestartRequired_Title") ?? "Restart required",
                Content = new TextBlock()
                {
                    Text = _localizer.GetLocalizedStringOrDefault("Settings_RestartRequired_Content") ??
                        "You'll need to restart the application for the theme to take effect or else the application will become unstable. " +
                        "This is most likely me not configuring the theming correctly. Dark Mode is the recommended theme.\n\n" +
                        "Sorry for the inconvenience.",
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Settings_RestartRequired_PrimaryButton") ?? "Restart",
                CloseButtonText = _localizer.GetLocalizedStringOrDefault("Settings_RestartRequired_CloseButton") ?? "Cancel",
                DefaultButton = ContentDialogButton.Primary
            });

            if (result != ContentDialogResult.Primary) return;

            ElementTheme = param;
            await _themeSelectorService.SetThemeAsync(param);
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Settings_Notification_RestartingTitle") ?? "Restarting...", _localizer.GetLocalizedStringOrDefault("Settings_Restart_Restarting") ?? "The application will restart now.",
                null);
            await RestartAppAsync();
        }
    }

    [RelayCommand]
    private async Task WindowSizePositionToggle(string? type)
    {
        if (type != "size" && type != "position") return;

        var windowSettings =
            await _localSettingsService.ReadOrCreateSettingAsync<ScreenSizeSettings>(ScreenSizeSettings.Key);

        if (type == "size")
        {
            PersistWindowSize = !PersistWindowSize;
            windowSettings.PersistWindowSize = PersistWindowSize;
        }
        else
        {
            PersistWindowPosition = !PersistWindowPosition;
            windowSettings.PersistWindowPosition = PersistWindowPosition;
        }

        await _localSettingsService.SaveSettingAsync(ScreenSizeSettings.Key, windowSettings).ConfigureAwait(false);
    }

    private static string GetVersionDescription()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version!;

        return
            $"{"AppDisplayName".GetLocalized()} - {VersionFormatter(version)}";
    }


    private bool ValidFolderSettings()
    {
        return PathToGIMIFolderPicker.IsValid && PathToModsFolderPicker.IsValid &&
               PathToGIMIFolderPicker.Path != PathToModsFolderPicker.Path &&
               (PathToGIMIFolderPicker.Path != _modManagerOptions?.GimiRootFolderPath ||
                PathToModsFolderPicker.Path != _modManagerOptions?.ModsFolderPath);
    }


    [RelayCommand(CanExecute = nameof(ValidFolderSettings))]
    private async Task SaveSettings()
    {
        var dialog = new ContentDialog();
        dialog.XamlRoot = App.MainWindow.Content.XamlRoot;
        dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
        dialog.Title = _localizer.GetLocalizedStringOrDefault("Settings_UpdateFolderPaths_Title") ?? "Update Folder Paths?";
        dialog.CloseButtonText = _localizer.GetLocalizedStringOrDefault("Settings_UpdateFolderPaths_CloseButton") ?? "Cancel";
        dialog.PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Settings_UpdateFolderPaths_PrimaryButton") ?? "Save";
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.Content = _localizer.GetLocalizedStringOrDefault("Settings_UpdateFolderPaths_Content") ?? "Do you want to save the new folder paths? The App will restart afterwards.";

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            var modManagerOptions = await _localSettingsService.ReadSettingAsync<ModManagerOptions>(
                ModManagerOptions.Section) ?? new ModManagerOptions();

            modManagerOptions.GimiRootFolderPath = PathToGIMIFolderPicker.Path;
            modManagerOptions.ModsFolderPath = PathToModsFolderPicker.Path;

            await _localSettingsService.SaveSettingAsync(ModManagerOptions.Section,
                modManagerOptions);
            _logger.Information("Saved startup settings: {@ModManagerOptions}", modManagerOptions);
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Settings_Restart_SettingsSaved") ?? "Settings saved. Restarting App...", "", TimeSpan.FromSeconds(2));


            await RestartAppAsync();
        }
    }

    [RelayCommand]
    private async Task BrowseGimiFolderAsync()
    {
        await PathToGIMIFolderPicker.BrowseFolderPathAsync(App.MainWindow);
        if (PathToGIMIFolderPicker.PathHasValue &&
            !PathToModsFolderPicker.PathHasValue)
            PathToModsFolderPicker.Path = Path.Combine(PathToGIMIFolderPicker.Path!, "Mods");
    }


    [RelayCommand]
    private async Task BrowseModsFolderAsync()
    {
        await PathToModsFolderPicker.BrowseFolderPathAsync(App.MainWindow);
    }

    [RelayCommand]
    private async Task ReorganizeModsAsync()
    {
        var result = await _windowManagerService.ShowDialogAsync(new ContentDialog()
        {
            Title = _localizer.GetLocalizedStringOrDefault("Settings_ReorganizeMods_Title") ?? "Reorganize Mods?",
            Content = new TextBlock()
            {
                Text = _localizer.GetLocalizedStringOrDefault("Settings_ReorganizeMods_Content") ??
                    "Do you want to reorganize the Mods folder?\n" +
                    "This will prompt the application to sort existing mods that are directly in the Mods folder and Others folder, into folders assigned to their respective characters.\n\n" +
                    "Any mods that can't be reasonably matched will be placed in an 'Others' folder. While the mods already in 'Others' folder will remain there.",
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true
            },
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Settings_ReorganizeMods_PrimaryButton") ?? "Yes",
            DefaultButton = ContentDialogButton.Primary,
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Settings_ReorganizeMods_CloseButton") ?? "Cancel",
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
        });

        if (result == ContentDialogResult.Primary)
        {
            _navigationViewService.IsEnabled = false;

            try
            {
                var movedModsCount = await Task.Run(() =>
                    _skinManagerService.ReorganizeModsAsync()); // Mods folder

                movedModsCount += await Task.Run(() =>
                    _skinManagerService.ReorganizeModsAsync(
                        _gameService.GetCharacterByIdentifier(_gameService.OtherCharacterInternalName)!
                            .InternalName)); // Others folder

                await _skinManagerService.RefreshModsAsync();

                if (movedModsCount == -1)
                    _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Settings_Mods_ReorganizeFailed") ?? "Mods reorganization failed.",
                        App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("Settings_SeeLogs") ?? "See logs for more details.", TimeSpan.FromSeconds(5));

                else
                    _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Settings_Mods_Reorganized") ?? "Mods reorganized.",
                        string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("Settings_ModsMovedMsg") ?? "Moved {0} mods to character folders", movedModsCount), TimeSpan.FromSeconds(5));
            }
            finally
            {
                _navigationViewService.IsEnabled = true;
            }
        }
    }


    private async Task RestartAppAsync(int delay = 2)
    {
        _navigationViewService.IsEnabled = false;

        await Task.Delay(TimeSpan.FromSeconds(delay));

        await _lifeCycleService.RestartAsync(notifyOnError: true);
    }

    private bool CanResetGenshinExePath()
    {
        return GenshinProcessManager.ProcessStatus != ProcessStatus.NotInitialized;
    }

    [RelayCommand(CanExecute = nameof(CanResetGenshinExePath))]
    private async Task ResetGenshinExePath()
    {
        await GenshinProcessManager.ResetProcessOptions();
    }

    private bool CanReset3DmigotoPath()
    {
        return ThreeDMigtoProcessManager.ProcessStatus != ProcessStatus.NotInitialized;
    }

    [RelayCommand(CanExecute = nameof(CanReset3DmigotoPath))]
    private async Task Reset3DmigotoPath()
    {
        await ThreeDMigtoProcessManager.ResetProcessOptions();
    }

    private void UpdateCheckerOnNewVersionAvailable(object? sender, UpdateChecker.NewVersionEventArgs e)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            if (e.Version == new Version())
            {
                // User clicked Ignore — hide the update badge
                ShowNewVersionAvailable = false;
                CanIgnoreUpdate = _updateChecker.LatestRetrievedVersion != _updateChecker.IgnoredVersion;
                return;
            }

            ShowNewVersionAvailable = true;
            LatestVersion = VersionFormatter(e.Version);

            if (_updateChecker.LatestRetrievedVersion != _updateChecker.IgnoredVersion)
                CanIgnoreUpdate = true;
        });
    }

    private static string VersionFormatter(Version version)
    {
        return $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    [RelayCommand(CanExecute = nameof(CanIgnoreUpdate))]
    private async Task IgnoreNewVersion()
    {
        await _updateChecker.IgnoreCurrentVersionAsync();
    }

    [ObservableProperty] private bool _exportingMods = false;
    [ObservableProperty] private int _exportProgress = 0;
    [ObservableProperty] private string _exportProgressText = string.Empty;
    [ObservableProperty] private string? _currentModName;

    [RelayCommand]
    private async Task ExportMods(ContentDialog contentDialog)
    {
        var dialog = new ContentDialog()
        {
            PrimaryButtonText = "Export",
            IsPrimaryButtonEnabled = true,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        dialog.Title = _localizer.GetLocalizedStringOrDefault("Settings_Export_ButtonText") ?? "Export Mods";
        dialog.PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Settings_Export_PrimaryButton") ?? "Export";
        dialog.CloseButtonText = _localizer.GetLocalizedStringOrDefault("Settings_Export_CloseButton") ?? "Cancel";

        dialog.ContentTemplate = contentDialog.ContentTemplate;

        var model = new ExportModsDialogModel(_gameService.GetAllModdableObjects());
        dialog.DataContext = model;
        var result = await _windowManagerService.ShowDialogAsync(dialog);

        if (result != ContentDialogResult.Primary)
            return;

        var folderPicker = new FolderPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
        folderPicker.FileTypeFilter.Add("*");
        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder == null)
            return;

        ExportingMods = true;
        _navigationViewService.IsEnabled = false;

        var charactersToExport =
            model.CharacterModsToBackup.Where(modList => modList.IsChecked).Select(ch => ch.Character);
        var modsList = new List<ICharacterModList>();
        foreach (var character in charactersToExport)
            modsList.Add(_skinManagerService.GetCharacterModList(character.InternalName));

        try
        {
            _skinManagerService.ModExportProgress += HandleProgressEvent;
            await Task.Run(() =>
            {
                _skinManagerService.ExportMods(modsList, folder.Path,
                    removeLocalJasmSettings: model.RemoveJasmSettings, zip: false,
                    keepCharacterFolderStructure: model.KeepFolderStructure, setModStatus: model.SetModStatus.Value);
            });
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Settings_Export_ModsExported") ?? "Mods exported", string.Format(_localizer.GetLocalizedStringOrDefault("Settings_Export_ModsExportedToFormat") ?? "Mods exported to {0}", folder.Path),
                TimeSpan.FromSeconds(5));
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error exporting mods");
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Settings_Export_ErrorExporting") ?? "Error exporting mods", e.Message, TimeSpan.FromSeconds(10));
        }
        finally
        {
            _skinManagerService.ModExportProgress -= HandleProgressEvent;
            ExportingMods = false;
            _navigationViewService.IsEnabled = true;
        }
    }

    private void HandleProgressEvent(object? sender, ExportProgress args)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            ExportProgress = args.Progress;
            ExportProgressText = args.Operation;
            CurrentModName = args.ModName;
        });
    }


    [RelayCommand]
    private async Task SelectLanguage(string selectedLanguageName)
    {
        if (_nameToLangCode.TryGetValue(selectedLanguageName, out var langCode))
        {
            if (langCode == _localizer.CurrentLanguage.LanguageCode)
                return;

            var restartDialog = new ContentDialog()
            {
                Title = _localizer.GetLocalizedStringOrDefault("Settings_RestartRequired_LangTitle") ?? "Restart Required",
                Content = new TextBlock()
                {
                    Text = _localizer.GetLocalizedStringOrDefault("/Settings/ChangeLanguageDialogText",
                        defaultValue:
                        "Changing the language requires a restart of the application.\n" +
                        "This is required to ensure that the application is configured correctly for the selected language.\n\n" +
                        "Do you want to change the language?"),
                    TextWrapping = TextWrapping.WrapWholeWords,
                    IsTextSelectionEnabled = true
                },
                PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Settings_RestartRequired_LangPrimaryButton") ?? "Change Language and restart",
                CloseButtonText = _localizer.GetLocalizedStringOrDefault("Settings_RestartRequired_LangCloseButton") ?? "Cancel",
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

            await RestartAppAsync();
        }
    }

    [ObservableProperty] private bool _updateDownloading;
    [ObservableProperty] private int _updateDownloadProgress;
    [ObservableProperty] private string _updateStatusText = string.Empty;
    public bool IsUpdateProgressIndeterminate => UpdateDownloading && UpdateDownloadProgress == 0;
    public bool IsUpdateButtonEnabled => !UpdateDownloading;

    private const string UpdateStagingFolder = "JASM_Update";
    private const string UpdateOldFolder = "JASM_Old";

    [RelayCommand]
    private async Task UpdateJasmAsync()
    {
        UpdateDownloading = true;
        UpdateStatusText = _localizer.GetLocalizedStringOrDefault("Settings_Update_Downloading") ?? "Downloading update...";

        try
        {
            var (downloadUrl, assetName) = await GetLatestReleaseDownloadUrlAsync();
            if (downloadUrl is null || assetName is null)
            {
                UpdateStatusText = _localizer.GetLocalizedStringOrDefault("Settings_Update_DownloadFailed") ?? "Could not find download URL";
                UpdateDownloading = false;
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "JASM_Update");
            var archivePath = Path.Combine(tempDir, assetName);

            Directory.CreateDirectory(tempDir);

            // Download in own scope so file handle is released before extraction
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "JASM-Update-Downloader");

                using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                var totalBytes = response.Content.Headers.ContentLength ?? -1;

                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = File.Create(archivePath);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;
                var lastProgressUpdate = DateTime.UtcNow;

                while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;

                    if (totalBytes > 0 && (DateTime.UtcNow - lastProgressUpdate).TotalMilliseconds > 200)
                    {
                        UpdateDownloadProgress = (int)(totalRead * 100 / totalBytes);
                        UpdateStatusText = string.Format(
                            _localizer.GetLocalizedStringOrDefault("Settings_Update_DownloadingProgress") ?? "Downloading... {0}%",
                            UpdateDownloadProgress);
                        lastProgressUpdate = DateTime.UtcNow;
                    }
                }
            } // file handle released here

            UpdateStatusText = _localizer.GetLocalizedStringOrDefault("Settings_Update_Extracting") ?? "Extracting update...";

            var installParent = new DirectoryInfo(App.ROOT_DIR).Parent!.FullName;
            var stagingPath = Path.Combine(installParent, UpdateStagingFolder);

            if (Directory.Exists(stagingPath))
                Directory.Delete(stagingPath, true);

            await Task.Run(() =>
            {
                if (assetName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
                {
                    // Use bundled 7z.exe — reliable and already shipped with the app
                    var sevenZip = Path.Combine(App.ASSET_DIR, "7z", "7z.exe");
                    if (!File.Exists(sevenZip))
                        throw new FileNotFoundException("7z.exe not found at " + sevenZip);

                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = sevenZip,
                        Arguments = $"x \"{archivePath}\" -o\"{stagingPath}\" -y",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    })!;
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        var err = process.StandardError.ReadToEnd();
                        throw new Exception($"7z extraction failed (code {process.ExitCode}): {err}");
                    }
                }
                else
                {
                    System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, stagingPath);
                }

                // Flatten: if there's a single inner JASM/ folder, move its contents up
                var innerDir = new DirectoryInfo(stagingPath).EnumerateDirectories()
                    .FirstOrDefault(d => d.Name.StartsWith("JASM", StringComparison.OrdinalIgnoreCase));
                if (innerDir is not null)
                {
                    foreach (var fsInfo in innerDir.EnumerateFileSystemInfos())
                    {
                        var dest = Path.Combine(stagingPath, fsInfo.Name);
                        if (fsInfo is DirectoryInfo dir)
                            dir.MoveTo(dest);
                        else
                            File.Move(fsInfo.FullName, dest);
                    }
                    innerDir.Delete(true);
                }
            });

            // Clean up temp
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }

            // Write and launch the swap script
            UpdateStatusText = _localizer.GetLocalizedStringOrDefault("Settings_Update_Restarting") ?? "Restarting to apply update...";

            var installDir = App.ROOT_DIR.TrimEnd(Path.DirectorySeparatorChar);
            var parentDir = new DirectoryInfo(installDir).Parent!.FullName;
            var installFolderName = Path.GetFileName(installDir);

            // Find the actual exe name from the staging folder (more reliable than process name)
            var stagingExe = Directory.EnumerateFiles(stagingPath, "*.exe", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => Path.GetFileName(f).StartsWith("JASM", StringComparison.OrdinalIgnoreCase));
            var exeName = stagingExe is not null ? Path.GetFileName(stagingExe) : "JASM - Just Another Skin Manager.exe";

            var scriptPath = Path.Combine(parentDir, "JASM_Update.cmd");
            var script = $@"@echo off
timeout /t 8 /nobreak > nul
cd /d ""{parentDir}""
if exist ""{UpdateOldFolder}"" rmdir /s /q ""{UpdateOldFolder}""
move ""{installFolderName}"" ""{UpdateOldFolder}""
move ""{UpdateStagingFolder}"" ""{installFolderName}""
start """" ""{installFolderName}\{exeName}""
del ""%~f0""
";

            _logger.Information("Update: installDir={InstallDir}", installDir);
            _logger.Information("Update: parentDir={ParentDir}", parentDir);
            _logger.Information("Update: installFolderName={FolderName}", installFolderName);
            _logger.Information("Update: exeName={ExeName}", exeName);
            _logger.Information("Update: stagingPath={StagingPath}", stagingPath);
            _logger.Information("Update: scriptPath={ScriptPath}", scriptPath);
            _logger.Information("Update: batch script content:\n{Script}", script);

            File.WriteAllText(scriptPath, script);
            _logger.Information("Update: batch file written, exists={Exists}", File.Exists(scriptPath));

            var psi = new ProcessStartInfo
            {
                FileName = scriptPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized
            };
            var startedProcess = Process.Start(psi);
            _logger.Information("Update: Process.Start returned: {Process}", startedProcess?.Id.ToString() ?? "null");

            Application.Current.Exit();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error during update process");
            UpdateStatusText = _localizer.GetLocalizedStringOrDefault("Settings_Update_ErrorStarting") ?? "Error during update";
            UpdateDownloading = false;
        }
    }

    private async Task<(Uri? url, string? fileName)> GetLatestReleaseDownloadUrlAsync()
    {
        const string releasesApiUrl = "https://api.github.com/repos/zurce/JASM/releases?per_page=2";

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.Add("User-Agent", "JASM-Update-Checker");

        var result = await client.GetAsync(releasesApiUrl);
        if (!result.IsSuccessStatusCode)
            return (null, null);

        var text = await result.Content.ReadAsStringAsync();
        var releases = Newtonsoft.Json.JsonConvert.DeserializeObject<GitHubRelease[]>(text) ?? Array.Empty<GitHubRelease>();

        var latest = releases
            .Where(r => !r.prerelease)
            .Where(r => TryParseVersion(r.tag_name) is not null)
            .OrderByDescending(r => TryParseVersion(r.tag_name))
            .FirstOrDefault();

        var asset = latest?.assets?.FirstOrDefault(a =>
        {
            var name = a.name ?? "";
            return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);
        });

        return asset?.browser_download_url is not null
            ? (new Uri(asset.browser_download_url), asset.name)
            : (null, null);
    }

    private static Version? TryParseVersion(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            return null;
        var versionString = tagName.StartsWith('v') ? tagName[1..] : tagName;
        return Version.TryParse(versionString, out var version) ? version : null;
    }

    private class GitHubRelease
    {
        public string? tag_name;
        public bool prerelease;
        public GitHubAsset[]? assets;
    }

    private class GitHubAsset
    {
        public string? name;
        public string? browser_download_url;
    }


    [RelayCommand]
    private async Task SelectGameAsync(string? game)
    {
        var jasmSelectedGame = await _selectedGameService.GetSelectedGameAsync();

        if (game.IsNullOrEmpty() || game == jasmSelectedGame)
            return;

        var switchGameDialog = new ContentDialog()
        {
            Title = _localizer.GetLocalizedStringOrDefault("Settings_SwitchGame_Title") ?? "Switch Game",
            Content = new TextBlock()
            {
                Text = _localizer.GetLocalizedStringOrDefault("Settings_SwitchGame_Content") ??
                    "Switching games will restart the application. " +
                    "This is required to ensure that the application is configured correctly for the selected game.\n\n" +
                    "Do you want to switch games?",
                TextWrapping = TextWrapping.WrapWholeWords
            },

            PrimaryButtonText = string.Format(_localizer.GetLocalizedStringOrDefault("Settings_SwitchGame_PrimaryButton") ?? "Switch to {0}", game),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Settings_SwitchGame_CloseButton") ?? "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await _windowManagerService.ShowDialogAsync(switchGameDialog);

        if (result != ContentDialogResult.Primary)
        {
            SelectedGame = game;
            return;
        }

        await _selectedGameService.SetSelectedGame(game);
        await RestartAppAsync(0).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ToggleCharacterSkinsAsCharacters()
    {
        var modManagerOptions =
            await _localSettingsService.ReadOrCreateSettingAsync<ModManagerOptions>(ModManagerOptions.Section);

        var result = await new CharacterSkinsDialog().ShowDialogAsync(modManagerOptions.CharacterSkinsAsCharacters);

        if (result != ContentDialogResult.Primary)
        {
            CharacterAsSkinsCheckbox = modManagerOptions.CharacterSkinsAsCharacters;
            return;
        }


        modManagerOptions.CharacterSkinsAsCharacters = !modManagerOptions.CharacterSkinsAsCharacters;

        await _localSettingsService.SaveSettingAsync(ModManagerOptions.Section, modManagerOptions);

        CharacterAsSkinsCheckbox = modManagerOptions.CharacterSkinsAsCharacters;

        await RestartAppAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private Task NavigateToCommandsSettings()
    {
        _navigationService.NavigateTo(typeof(CommandsSettingsViewModel).FullName!,
            transitionInfo: new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ToggleModUpdateChecker()
    {
        var modUpdateCheckerSettings =
            await _localSettingsService.ReadOrCreateSettingAsync<BackGroundModCheckerSettings>(
                BackGroundModCheckerSettings.Key);

        await Task.Run(async () =>
        {
            if (modUpdateCheckerSettings.Enabled)
                await _modUpdateAvailableChecker.DisableAutoCheckerAsync();
            else
                await _modUpdateAvailableChecker.EnableAutoCheckerAsync();

            await Task.Delay(1000).ConfigureAwait(false);
        });

        modUpdateCheckerSettings = await _localSettingsService.ReadOrCreateSettingAsync<BackGroundModCheckerSettings>(
            BackGroundModCheckerSettings.Key);

        IsModUpdateCheckerEnabled = modUpdateCheckerSettings.Enabled;
    }

    public async void OnNavigatedTo(object parameter)
    {
        SelectedGame = await _selectedGameService.GetSelectedGameAsync();
        var modUpdateCheckerOptions =
            await _localSettingsService.ReadOrCreateSettingAsync<BackGroundModCheckerSettings>(
                BackGroundModCheckerSettings.Key);

        IsModUpdateCheckerEnabled = modUpdateCheckerOptions.Enabled;
        var gameInfo = await GameService.GetGameInfoAsync(Enum.Parse<SupportedGames>(SelectedGame));

        if (gameInfo is not null)
        {
            var folderWarning = _localizer.GetLocalizedStringOrDefault("Settings_FolderWarning_No3DMigotoEntry") ?? "Folder does not contain any entry with the specified names:";
            PathToGIMIFolderPicker.SetValidators(GimiFolderRootValidators.Validators(gameInfo.GameModelImporterExeNames, folderWarning));
        }

        var windowSettings =
            await _localSettingsService.ReadOrCreateSettingAsync<ScreenSizeSettings>(ScreenSizeSettings.Key);

        var characterDetailsSettings = await _localSettingsService.ReadCharacterDetailsSettingsAsync(SettingScope.App);

        PersistWindowSize = windowSettings.PersistWindowSize;
        PersistWindowPosition = windowSettings.PersistWindowPosition;
        await GenshinProcessManager.TryInitialize();
        await ThreeDMigtoProcessManager.TryInitialize();
        ModCacheSizeGB = _modArchiveRepository.GetTotalCacheSizeInGB().ToString("F");
    }

    [ObservableProperty] private string _maxCacheSizeString = string.Empty;

    private void SetCacheString(int value)
    {
        MaxCacheSizeString = $"{value} GB";
    }

    [RelayCommand]
    private async Task SetCacheLimit(int maxValue)
    {
        var modArchiveSettings =
            await _localSettingsService.ReadOrCreateSettingAsync<ModArchiveSettings>(ModArchiveSettings.Key);

        modArchiveSettings.MaxLocalArchiveCacheSizeGb = maxValue;

        await _localSettingsService.SaveSettingAsync(ModArchiveSettings.Key, modArchiveSettings);

        MaxCacheLimit = maxValue;
        SetCacheString(maxValue);
    }


    [RelayCommand]
    private static Task ShowCleanModsFolderDialogAsync()
    {
        var dialog = new ClearEmptyFoldersDialog();
        return dialog.ShowDialogAsync();
    }


    [RelayCommand]
    private Task ShowDisableAllModsDialogAsync()
    {
        var dialog = new DisableAllModsDialog();
        return dialog.ShowDialogAsync();
    }

    public void OnNavigatedFrom()
    {
    }
}

public partial class ExportModsDialogModel : ObservableObject
{
    [ObservableProperty] private bool _zipMods = false;
    [ObservableProperty] private bool _keepFolderStructure = true;

    [ObservableProperty] private bool _removeJasmSettings = false;

    public ObservableCollection<CharacterCheckboxModel> CharacterModsToBackup { get; set; } = new();

    public ObservableCollection<ModStatusOption> SetModStatuses { get; set; } = new()
    {
        new(GIMI_ModManager.Core.Contracts.Services.SetModStatus.KeepCurrent, "Settings_Export_Status_KeepCurrent"),
        new(GIMI_ModManager.Core.Contracts.Services.SetModStatus.EnableAllMods, "Settings_Export_Status_EnableAll"),
        new(GIMI_ModManager.Core.Contracts.Services.SetModStatus.DisableAllMods, "Settings_Export_Status_DisableAll")
    };

    [ObservableProperty] private ModStatusOption _setModStatus = null!;

    public ExportModsDialogModel(IEnumerable<IModdableObject> characters)
    {
        SetModStatus = SetModStatuses[0];
        foreach (var character in characters) CharacterModsToBackup.Add(new CharacterCheckboxModel(character));
    }

    public class ModStatusOption
    {
        public SetModStatus Value { get; }
        public string DisplayName { get; }

        public ModStatusOption(SetModStatus value, string resourceKey)
        {
            Value = value;
            var localizer = App.GetService<ILanguageLocalizer>();
            DisplayName = localizer.GetLocalizedStringOrDefault(resourceKey) ?? value.ToString();
        }

        public override string ToString() => DisplayName;
    }
}

public partial class CharacterCheckboxModel : ObservableObject
{
    [ObservableProperty] private bool _isChecked = true;
    [ObservableProperty] private IModdableObject _character;

    public CharacterCheckboxModel(IModdableObject character)
    {
        _character = character;
    }
}