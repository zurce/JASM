using System.Collections.ObjectModel;
using System.Diagnostics;
using Windows.Storage;
using Windows.System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkitWrapper;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.GamesService.Interfaces;
using GIMI_ModManager.Core.GamesService.Models;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.Core.Services;
using GIMI_ModManager.WinUI.Models.Options;
using GIMI_ModManager.Core.Services.GameBanana;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Contracts.ViewModels;
using GIMI_ModManager.WinUI.Helpers;
using GIMI_ModManager.WinUI.Models;
using GIMI_ModManager.WinUI.Models.Settings;
using GIMI_ModManager.WinUI.Models.ViewModels;
using GIMI_ModManager.WinUI.Services;
using GIMI_ModManager.WinUI.Services.ModHandling;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels;
using GIMI_ModManager.WinUI.ViewModels.SubVms;
using GIMI_ModManager.WinUI.Views.CharacterDetailsPages;
using Serilog;

namespace GIMI_ModManager.WinUI.ViewModels;

/// <summary>State of the XXMI launch / running indicator for the current game.</summary>
public enum XxmiProcessState
{
    /// <summary>No XXMI process is running; the buttons are ready to launch.</summary>
    Idle,
    /// <summary>A launch was just started; waiting for it to spin up.</summary>
    Launching,
    /// <summary>An XXMI Launcher process is currently alive.</summary>
    Running
}

public partial class CharactersViewModel : ObservableRecipient, INavigationAware
{
    private readonly IGameService _gameService;
    private readonly ILogger _logger;
    private readonly INavigationService _navigationService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILanguageLocalizer _localizer;
    private readonly ModDragAndDropService _modDragAndDropService;
    private readonly ModNotificationManager _modNotificationManager;
    private readonly ModCrawlerService _modCrawlerService;
    private readonly ModSettingsService _modSettingsService;
    private readonly ModUpdateAvailableChecker _modUpdateAvailableChecker;
    private readonly ModPresetHandlerService _modPresetHandlerService;
    private readonly BusyService _busyService;
    private readonly ModRandomizationService _modRandomizationService;

    public readonly GenshinProcessManager GenshinProcessManager;
    public readonly ThreeDMigtoProcessManager ThreeDMigtoProcessManager;

    public readonly string StartGameIcon;
    public readonly string ShortGameName;
    public NotificationManager NotificationManager { get; }

    public OverviewDockPanelVM DockPanelVM { get; }

    public SimpleSelectProcessDialogVM SimpleSelectProcessDialogVM { get; } = new();


    private IReadOnlyList<IModdableObject> _characters = new List<IModdableObject>();

    private IReadOnlyList<CharacterGridItemModel> _backendCharacters = new List<CharacterGridItemModel>();
    public ObservableCollection<CharacterGridItemModel> SuggestionsBox { get; } = new();
    public ObservableCollection<CharactersViewModels.ModPresetEntryVm> ModPresets { get; } = new();
    public ObservableCollection<CharacterGridItemModel> Characters { get; } = new();

    private string _searchText = string.Empty;

    private readonly Dictionary<FilterType, GridFilter> _filters = new();


    public ObservableCollection<GridItemSortingMethod> SortingMethods { get; } = new();

    [ObservableProperty] private GridItemSortingMethod _selectedSortingMethod;
    [ObservableProperty] private bool _sortByDescending;

    [ObservableProperty] private bool _canCheckForUpdates = false;

    [ObservableProperty] private Uri? _gameBananaLink;

    /// <summary>
    /// True when the active game is treated as an XXMI-managed installation, in which case
    /// the legacy Start 3DMigoto / Start Game buttons are suppressed and an "Open XXMI"
    /// button is shown in their place.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLegacyStartButtons))]
    [NotifyPropertyChangedFor(nameof(CanOpenXxmi))]
    [NotifyPropertyChangedFor(nameof(CanLaunchXxmi))]
    [NotifyPropertyChangedFor(nameof(XxmiControlsVisibility))]
    private bool _isXxmiManaged;

    /// <summary>Shows the XXMI launch controls when the game is XXMI-managed.</summary>
    public bool XxmiControlsVisibility => IsXxmiManaged;

    /// <summary>
    /// True while an XXMI launch is in progress, so the XXMI buttons are disabled and a second
    /// click can't spawn a duplicate XXMI instance (which can break it).
    /// </summary>
    /// <summary>True while an XXMI launch is starting (short window during startup).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(XxmiProcessState))]
    [NotifyPropertyChangedFor(nameof(IsXxmiControlsEnabled))]
    [NotifyPropertyChangedFor(nameof(XxmiLaunchButtonText))]
    private bool _isLaunchingXxmi;

    /// <summary>True while an XXMI Launcher process is detected as alive (polled).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(XxmiProcessState))]
    [NotifyPropertyChangedFor(nameof(IsXxmiControlsEnabled))]
    [NotifyPropertyChangedFor(nameof(XxmiLaunchButtonText))]
    private bool _xxmiIsProcessRunning;

    /// <summary>
    /// Drives the XXMI buttons: Launching during startup, Running while an XXMI process is alive,
    /// Idle (ready) otherwise. Enabling the buttons only when Idle makes the state obvious.
    /// </summary>
    public XxmiProcessState XxmiProcessState => IsLaunchingXxmi
        ? XxmiProcessState.Launching
        : XxmiIsProcessRunning ? XxmiProcessState.Running
            : XxmiProcessState.Idle;

    /// <summary>True when the XXMI buttons are enabled (only when idle and no process running).</summary>
    public bool IsXxmiControlsEnabled => XxmiProcessState == XxmiProcessState.Idle;

    /// <summary>
    /// Text for the XXMI "Launch &lt;Game&gt;" button. Uses the XXMI importer code (e.g.
    /// "Launch GIMI"), and shows "Running" / "Launching..." while the process is active.
    /// </summary>
    public string XxmiLaunchButtonText => XxmiProcessState switch
    {
        XxmiProcessState.Running => _localizer.GetLocalizedStringOrDefault("CharactersPage_XxmiRunning") ?? "Running",
        XxmiProcessState.Launching => _localizer.GetLocalizedStringOrDefault("CharactersPage_XxmiLaunching") ?? "Launching...",
        _ => string.Format(_localizer.GetLocalizedStringOrDefault("CharactersPage_XxmiLaunch") ?? "Launch {0}",
                XxmiGameIdentifier ?? _gameService.GameShortName)
    };

    /// <summary>Resolved path to the XXMI Launcher executable, or null if unavailable.</summary>
    public string? XxmiLauncherExePath { get; private set; }

    /// <summary>XXMI importer identifier for the current game (e.g. GIMI / SRMI / ZZMI).</summary>
    public string? XxmiGameIdentifier { get; private set; }

    /// <summary>Resource path to the per-game XXMI icon (e.g. ms-appx:///Assets/Xxmi/GIMI.ico).</summary>
    public string? XxmiGameIcon { get; private set; }

    /// <summary>Resource path to the generic XXMI window icon for the Open XXMI button.</summary>
    public string XxmiLauncherIcon { get; } = "ms-appx:///Assets/Xxmi/window-icon.ico";

    /// <summary>True when the XXMI launcher is available to open.</summary>
    public bool CanOpenXxmi => IsXxmiManaged && !string.IsNullOrWhiteSpace(XxmiLauncherExePath) &&
                              File.Exists(XxmiLauncherExePath);

    /// <summary>
    /// True when the game can be launched through XXMI (XXMI-managed, launcher present, and
    /// the game has a known XXMI identifier).
    /// </summary>
    public bool CanLaunchXxmi => CanOpenXxmi && !string.IsNullOrWhiteSpace(XxmiGameIdentifier);

    /// <summary>
    /// True when the legacy Start 3DMigoto / Start Game buttons should be shown (i.e. the
    /// game is NOT XXMI-managed).
    /// </summary>
    public bool ShowLegacyStartButtons => !IsXxmiManaged;
    [ObservableProperty] private string _categoryPageTitle = string.Empty;
    [ObservableProperty] private string _modToggleText = string.Empty;
    [ObservableProperty] private string _modEnabledToggleText = string.Empty;
    [ObservableProperty] private string _modNotificationsToggleText = string.Empty;
    [ObservableProperty] private string _searchBoxPlaceHolder = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(ApplyPresetCommand))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    private bool _isNavigating = true;

    public CharactersViewModel(IGameService gameService, ILogger logger, INavigationService navigationService,
        ISkinManagerService skinManagerService, ILocalSettingsService localSettingsService,
        NotificationManager notificationManager,
        GenshinProcessManager genshinProcessManager, ThreeDMigtoProcessManager threeDMigtoProcessManager,
        ModDragAndDropService modDragAndDropService, ModNotificationManager modNotificationManager,
        ModCrawlerService modCrawlerService, ModSettingsService modSettingsService,
        ModUpdateAvailableChecker modUpdateAvailableChecker, ModPresetHandlerService modPresetHandlerService,
        BusyService busyService, ILanguageLocalizer localizer, ModRandomizationService modRandomizationService)
    {
        _gameService = gameService;
        _logger = logger.ForContext<CharactersViewModel>();
        _navigationService = navigationService;
        _skinManagerService = skinManagerService;
        _localSettingsService = localSettingsService;
        NotificationManager = notificationManager;
        GenshinProcessManager = genshinProcessManager;
        ThreeDMigtoProcessManager = threeDMigtoProcessManager;
        _modDragAndDropService = modDragAndDropService;
        _modNotificationManager = modNotificationManager;
        _modCrawlerService = modCrawlerService;
        _modSettingsService = modSettingsService;
        _modUpdateAvailableChecker = modUpdateAvailableChecker;
        _modPresetHandlerService = modPresetHandlerService;
        _busyService = busyService;
        _localizer = localizer;
        _modRandomizationService = modRandomizationService;

        _modNotificationManager.OnModNotification += (_, _) =>
            App.MainWindow.DispatcherQueue.EnqueueAsync(RefreshNotificationsAsync);

        DockPanelVM = new OverviewDockPanelVM();
        StartGameIcon = _gameService.GameIcon;
        ShortGameName = $"{_localizer.GetLocalizedStringOrDefault("CharactersPage_StartGamePrefix") ?? "Start"} {_gameService.GameShortName}";
        GameBananaLink = _gameService.GameBananaUrl;

        // Determine whether the active game is managed as an XXMI installation.
        var modManagerOptions = _localSettingsService.ReadSetting<ModManagerOptions>(ModManagerOptions.Section);
        IsXxmiManaged = modManagerOptions?.TreatAsXXMI ?? false;
        XxmiLauncherExePath = XxmiInstallationDetector.TryResolveLauncherExe();

        // Resolve the current game's XXMI importer identifier + icon for the Launch button.
        if (Enum.TryParse<SupportedGames>(_gameService.GameShortName, out var supportedGame))
        {
            XxmiGameIdentifier = XxmiInstallationDetector.GetXxmiGameIdentifier(supportedGame);
            XxmiGameIcon = XxmiGameIdentifier is null
                ? null
                : $"ms-appx:///Assets/Xxmi/{XxmiGameIdentifier}.ico";
        }

        StartXxmiProcessPoller();

        CanCheckForUpdates = _modUpdateAvailableChecker.IsReady;
        _modUpdateAvailableChecker.OnUpdateCheckerEvent += (_, _) =>
        {
            App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
            {
                CanCheckForUpdates = _modUpdateAvailableChecker.IsReady;
                return Task.CompletedTask;
            });
        };

        IsBusy = _busyService.IsPageBusy(this);
    }

    public event EventHandler<ScrollToCharacterArgs>? OnScrollToCharacter;

    private void FilterElementSelected(object? sender, FilterElementSelectedArgs e)
    {
        if (_category.ModCategory != ModCategory.Character)
            return;

        if (e.InternalElementNames.Length == 0)
        {
            _filters.Remove(FilterType.Element);
            ResetContent();
            return;
        }

        _filters[FilterType.Element] = new GridFilter(character =>
            e.InternalElementNames.Contains(((ICharacter)character.Character).Element.InternalName));
        ResetContent();
    }

    private CharacterGridItemModel NoCharacterFound =>
        new(new Character("None", string.Format(_localizer.GetLocalizedStringOrDefault("CharactersPage_NoCategoryFound") ?? "No {0} Found...", _localizer.GetLocalizedStringOrDefault("Category_" + _category.DisplayNamePlural.Replace(" ", "")) ?? _category.DisplayNamePlural)));

    public void AutoSuggestBox_TextChanged(string text)
    {
        _searchText = text;
        SuggestionsBox.Clear();

        if (string.IsNullOrWhiteSpace(_searchText))
        {
            SuggestionsBox.Clear();
            _filters.Remove(FilterType.Search);
            ResetContent();
            return;
        }

        var suitableItems = _gameService.QueryModdableObjects(text, category: _category, minScore: 120)
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(x => new CharacterGridItemModel(x.Key))
            .ToList();


        if (!suitableItems.Any())
        {
            SuggestionsBox.Add(NoCharacterFound);
            _filters.Remove(FilterType.Search);
            ResetContent();
            return;
        }

        suitableItems.ForEach(suggestion => SuggestionsBox.Add(suggestion));

        _filters[FilterType.Search] = new GridFilter(character => SuggestionsBox.Contains(character));
        ResetContent();
    }


    public async Task SuggestionBox_Chosen(CharacterGridItemModel? character)
    {
        if (character == NoCharacterFound || character is null)
            return;


        await CharacterClicked(character);
    }

    private void ResetContent()
    {
        if (_isNavigating) return;

        var filteredCharacters = FilterCharacters(_backendCharacters);
        var sortedCharacters = SelectedSortingMethod.Sort(filteredCharacters, SortByDescending).ToList();

        var charactersToRemove = Characters.Except(sortedCharacters).ToArray();

        if (Characters.Count == 0)
        {
            foreach (var characterGridItemModel in sortedCharacters)
            {
                Characters.Add(characterGridItemModel);
            }

            return;
        }

        var missingCharacters = sortedCharacters.Except(Characters);

        foreach (var characterGridItemModel in missingCharacters)
        {
            Characters.Add(characterGridItemModel);
        }

        foreach (var characterGridItemModel in sortedCharacters)
        {
            var newIndex = sortedCharacters.IndexOf(characterGridItemModel);
            var oldIndex = Characters.IndexOf(characterGridItemModel);
            //Check if character is already at the right index

            if (newIndex == Characters.IndexOf(characterGridItemModel)) continue;

            if (oldIndex < 0 || oldIndex >= Characters.Count || newIndex < 0 || newIndex >= Characters.Count)
                throw new ArgumentOutOfRangeException();

            Characters.RemoveAt(oldIndex);
            Characters.Insert(newIndex, characterGridItemModel);
        }


        foreach (var characterGridItemModel in charactersToRemove)
        {
            Characters.Remove(characterGridItemModel);
        }


        Debug.Assert(Characters.Distinct().Count() == Characters.Count,
            $"Characters.Distinct().Count(): {Characters.Distinct().Count()} != Characters.Count: {Characters.Count}\n\t" +
            $"Duplicate characters found in character overview");
    }

    private IEnumerable<CharacterGridItemModel> FilterCharacters(
        IReadOnlyList<CharacterGridItemModel> characters)
    {
        if (!_filters.Any())
        {
            foreach (var characterGridItemModel in characters)
            {
                yield return characterGridItemModel;
            }
        }

        var modsFoundForFilter = new Dictionary<FilterType, IEnumerable<CharacterGridItemModel>>();


        foreach (var filter in _filters)
        {
            modsFoundForFilter.Add(filter.Key, filter.Value.Filter(characters));
        }


        IEnumerable<CharacterGridItemModel>? intersectedMods = null;

        foreach (var kvp in modsFoundForFilter)
        {
            intersectedMods = intersectedMods == null
                ? kvp.Value
                : intersectedMods.Intersect(kvp.Value);
        }


        foreach (var characterGridItemModel in intersectedMods ?? Array.Empty<CharacterGridItemModel>())
        {
            yield return characterGridItemModel;
        }
    }

    private ICategory _category = null!;

    public async void OnNavigatedTo(object parameter)
    {
        if (parameter is not ICategory category)
        {
            _logger.Error("Invalid parameter type {ParameterType}", parameter?.GetType().FullName);
            category = _gameService.GetCategories().First();
        }

        _busyService.BusyChanged += OnBusyChangedHandler;

        _category = category;
        var catName = _localizer.GetLocalizedStringOrDefault("Category_" + category.DisplayName.Replace(" ", "")) ?? category.DisplayName;
        var catNamePlural = _localizer.GetLocalizedStringOrDefault("Category_" + category.DisplayNamePlural.Replace(" ", "")) ?? category.DisplayNamePlural;
        var pageTitleFormat = _localizer.GetLocalizedStringOrDefault("CharactersPage_TitleFormat") ?? "{0} Overview";
        CategoryPageTitle = string.Format(pageTitleFormat, catName);
        ModToggleText = $"{_localizer.GetLocalizedStringOrDefault("CharactersPage_ModToggleText") ?? "Show only"} {catNamePlural} {_localizer.GetLocalizedStringOrDefault("CharactersPage_ModToggleSuffix") ?? "with Mods"}";
        ModEnabledToggleText = $"{_localizer.GetLocalizedStringOrDefault("CharactersPage_ModEnabledToggleText") ?? "Show only"} {catNamePlural} {_localizer.GetLocalizedStringOrDefault("CharactersPage_ModEnabledToggleSuffix") ?? "with Enabled Mods"}";
        ModNotificationsToggleText = $"{_localizer.GetLocalizedStringOrDefault("CharactersPage_ModNotificationsToggleText") ?? "Show only"} {catNamePlural} {_localizer.GetLocalizedStringOrDefault("CharactersPage_ModNotificationsToggleSuffix") ?? "with Mod Notifications"}";
        SearchBoxPlaceHolder = $"{_localizer.GetLocalizedStringOrDefault("CharactersPage_SearchPrefix") ?? "Search"} {catNamePlural}...";


        var characters = _gameService.GetModdableObjects(_category);

        var firstType = characters.FirstOrDefault()?.GetType();
        if (characters.Any(ch => ch.GetType() != firstType))
            throw new InvalidOperationException("Characters must be of the same type");

        var others =
            characters.FirstOrDefault(ch =>
                ch.InternalName.Id.Contains("Others", StringComparison.OrdinalIgnoreCase));
        if (others is not null) // Add to front
        {
            characters.Remove(others);
            characters.Insert(0, others);
        }

        var gliders =
            characters.FirstOrDefault(ch => ch.InternalNameEquals(_gameService.GlidersCharacterInternalName));
        if (gliders is not null) // Add to end
        {
            characters.Remove(gliders);
            characters.Add(gliders);
        }

        var weapons =
            characters.FirstOrDefault(ch => ch.InternalNameEquals(_gameService.WeaponsCharacterInternalName));
        if (weapons is not null) // Add to end
        {
            characters.Remove(weapons);
            characters.Add(weapons);
        }


        _characters = characters;

        characters = new List<IModdableObject>(_characters);

        var pinnedCharactersOptions = await ReadCharacterSettings();

        var backendCharacters = new List<CharacterGridItemModel>();
        foreach (var pinedCharacterId in pinnedCharactersOptions.PinedCharacters)
        {
            var character = characters.FirstOrDefault(x => x.InternalNameEquals(pinedCharacterId));
            if (character is not null)
            {
                backendCharacters.Add(new CharacterGridItemModel(character) { IsPinned = true });
                characters.Remove(character);
            }
        }

        foreach (var hiddenCharacterId in pinnedCharactersOptions.HiddenCharacters)
        {
            var character = characters.FirstOrDefault(x => x.InternalNameEquals(hiddenCharacterId));
            if (character is not null)
            {
                backendCharacters.Add(new CharacterGridItemModel(character) { IsHidden = true });
                characters.Remove(character);
            }
        }

        // Add rest of characters
        foreach (var character in characters)
        {
            backendCharacters.Add(new CharacterGridItemModel(character));
        }

        _backendCharacters = backendCharacters;

        foreach (var characterGridItemModel in _backendCharacters)
        {
            var modList = _skinManagerService.GetCharacterModList(characterGridItemModel.Character);

            var characterModItems = new List<CharacterModItem>();
            foreach (var skinModEntry in modList.Mods)
            {
                var modSettings = await skinModEntry.Mod.Settings.TryReadSettingsAsync(true);

                characterModItems.Add(new CharacterModItem(skinModEntry.Mod.GetDisplayName(), skinModEntry.IsEnabled, modSettings?.DateAdded ?? default));
            }

            characterGridItemModel.SetMods(characterModItems);
        }


        InitializeSorters();

        if (typeof(ICharacter).IsAssignableFrom(firstType))
        {
            var backendCharactersList = _backendCharacters.Select(x => x.Character).Cast<ICharacter>().ToList();
            var distinctReleaseDates = backendCharactersList
                .Where(ch => ch.ReleaseDate != default)
                .DistinctBy(ch => ch.ReleaseDate)
                .Count();

            if (distinctReleaseDates == 1 &&
                SortingMethods.FirstOrDefault(x => x.SortingMethodType == GridItemSorter.ReleaseDateSortName) is
                { } releaseDateSortingMethod)
            {
                SortingMethods.Remove(releaseDateSortingMethod);
            }

            DockPanelVM.Initialize();
            DockPanelVM.FilterElementSelected += FilterElementSelected;
        }

        var modPresets = await _modPresetHandlerService.GetModPresetsAsync();
        modPresets.ForEach(preset =>
            ModPresets.Add(new CharactersViewModels.ModPresetEntryVm(preset.Name, ApplyPresetCommand)));

        // Add notifications
        await RefreshNotificationsAsync();

        // Character Ids where more than 1 skin is enabled
        await RefreshMultipleModsWarningAsync();

        // ShowOnlyModsCharacters
        var settings =
            await _localSettingsService
                .ReadOrCreateSettingAsync<CharacterOverviewSettings>(CharacterOverviewSettings.GetKey(_category));
        if (settings.ShowOnlyCharactersWithMods)
        {
            ShowOnlyCharactersWithMods = true;
            _filters[FilterType.HasMods] = new GridFilter(characterGridItem =>
                _skinManagerService.GetCharacterModList(characterGridItem.Character).Mods.Any());
        }

        if (settings.ShowOnlyModsWithNotifications)
        {
            ShowOnlyModsWithNotifications = true;
            _filters[FilterType.HasModNotifications] =
                new GridFilter(characterGridItemModel => characterGridItemModel.Notification);
        }

        if (settings.ShowOnlyCharactersWithEnabledMods)
        {
            ShowOnlyWithEnabledMods = true;
            _filters[FilterType.HasEnabledMods] = new GridFilter(characterGridItemModel => characterGridItemModel.Mods.Any(m => m.IsEnabled));
        }


        SortByDescending = settings.SortByDescending;

        var sorter = SortingMethods.FirstOrDefault(x => x.SortingMethodType == settings.SortingMethod);

        SelectedSortingMethod = sorter ?? SortingMethods.First();


        _isNavigating = false;
        ResetContent();

        var lastPageType = _navigationService.GetNavigationHistory().SkipLast(1).LastOrDefault();
        if (lastPageType?.PageType == typeof(CharacterDetailsPage) ||
            lastPageType?.PageType == typeof(CharacterDetailsViewModel))
        {
            InternalName? internalName = null;

            if (lastPageType.Parameter is CharacterGridItemModel characterGridModel)
            {
                internalName = characterGridModel.Character.InternalName;
            }
            else if (lastPageType.Parameter is INameable modObject)
            {
                internalName = modObject.InternalName;
            }
            else if (lastPageType.Parameter is string modObjectString)

            {
                internalName = new InternalName(modObjectString);
            }

            if (internalName is null)
                return;

            var characterGridItemModel = FindCharacterByInternalName(internalName);
            if (characterGridItemModel is not null)
            {
                OnScrollToCharacter?.Invoke(this, new ScrollToCharacterArgs(characterGridItemModel));
            }
        }
    }

    private async Task RefreshMultipleModsWarningAsync()
    {
        var charactersWithMultipleMods = _skinManagerService.CharacterModLists
            .Where(x => x.Mods.Count(mod => mod.IsEnabled) > 1);

        var charactersWithMultipleActiveSkins = new HashSet<InternalName>();
        await Task.Run(async () =>
        {
            foreach (var modList in charactersWithMultipleMods)
            {
                if (_gameService.IsMultiMod(modList.Character))
                    continue;

                if (modList.Character is ICharacter { Skins.Count: > 1 } character)
                {
                    var addWarning = false;
                    var subSkinsFound = new List<ICharacterSkin>();
                    foreach (var characterSkinEntry in modList.Mods)
                    {
                        if (!characterSkinEntry.IsEnabled) continue;

                        var subSkin = _modCrawlerService.GetFirstSubSkinRecursive(characterSkinEntry.Mod.FullPath);
                        var modSettingsResult = await _modSettingsService.GetSettingsAsync(characterSkinEntry.Id);


                        var mod = ModModel.FromMod(characterSkinEntry);


                        if (modSettingsResult.IsT0)
                            mod.WithModSettings(modSettingsResult.AsT0);

                        if (!mod.CharacterSkinOverride.IsNullOrEmpty())
                            subSkin = _gameService.GetCharacterByIdentifier(character.InternalName)?.Skins
                                .FirstOrDefault(x => SkinVM.FromSkin(x).InternalNameEquals(mod.CharacterSkinOverride));

                        if (subSkin is null)
                            continue;


                        if (subSkinsFound.All(foundSubSkin =>
                                !subSkin.InternalNameEquals(foundSubSkin)))
                        {
                            subSkinsFound.Add(subSkin);
                            continue;
                        }


                        addWarning = true;
                        break;
                    }

                    if (addWarning || subSkinsFound.Count > 1 && character.Skins.Count == 1)
                        charactersWithMultipleActiveSkins.Add(modList.Character.InternalName);
                }
                else if (modList.Mods.Count(modEntry => modEntry.IsEnabled) >= 2)
                {
                    charactersWithMultipleActiveSkins.Add(modList.Character.InternalName);
                }
            }
        });


        foreach (var characterGridItemModel in _backendCharacters)
        {
            if (charactersWithMultipleActiveSkins.Contains(characterGridItemModel.Character.InternalName))
            {
                if (_gameService.IsMultiMod(characterGridItemModel.Character))
                    continue;

                characterGridItemModel.Warning = true;
            }
            else
            {
                characterGridItemModel.Warning = false;
            }
        }
    }

    private async Task RefreshNotificationsAsync()
    {
        foreach (var character in _characters)
        {
            var characterGridItemModel = FindCharacterByInternalName(character.InternalName);
            if (characterGridItemModel is null) continue;

            var characterMods = _skinManagerService.GetCharacterModList(character).Mods;

            var notifications = new List<ModNotification>();
            foreach (var characterSkinEntry in characterMods)
            {
                var modNotification = await _modNotificationManager.GetNotificationsForModAsync(characterSkinEntry.Id);
                notifications.AddRange(modNotification);
            }

            if (!notifications.Any())
            {
                characterGridItemModel.Notification = false;
                characterGridItemModel.NotificationType = AttentionType.None;
            }

            foreach (var modNotification in notifications)
            {
                if (modNotification.AttentionType == AttentionType.Added ||
                    modNotification.AttentionType == AttentionType.UpdateAvailable)
                {
                    characterGridItemModel.Notification = true;
                    characterGridItemModel.NotificationType = modNotification.AttentionType;
                }
            }
        }
    }

    public void OnNavigatedFrom()
    {
        _busyService.BusyChanged -= OnBusyChangedHandler;
    }

    [RelayCommand]
    private Task CharacterClicked(CharacterGridItemModel characterModel)
    {
        _navigationService.SetListDataItemForNextConnectedAnimation(characterModel);

        _navigationService.NavigateToCharacterDetails(characterModel.Character.InternalName);

        return Task.CompletedTask;
    }

    [ObservableProperty] private bool _showOnlyCharactersWithMods = false;

    [RelayCommand]
    private async Task ShowCharactersWithModsAsync()
    {
        if (ShowOnlyCharactersWithMods)
        {
            ShowOnlyCharactersWithMods = false;

            _filters.Remove(FilterType.HasMods);

            ResetContent();
            var settingss = await ReadCharacterSettings();


            settingss.ShowOnlyCharactersWithMods = ShowOnlyCharactersWithMods;

            await SaveCharacterSettings(settingss);

            return;
        }

        _filters[FilterType.HasMods] = new GridFilter(characterGridItem =>
            _skinManagerService.GetCharacterModList(characterGridItem.Character.InternalName).Mods.Any());

        ShowOnlyCharactersWithMods = true;

        ResetContent();

        var settings = await ReadCharacterSettings();

        settings.ShowOnlyCharactersWithMods = ShowOnlyCharactersWithMods;

        await SaveCharacterSettings(settings).ConfigureAwait(false);
    }

    [ObservableProperty] private bool _showOnlyModsWithNotifications;

    [RelayCommand]
    private async Task ShowOnlyCharactersWithModNotificationsAsync()
    {
        if (ShowOnlyModsWithNotifications)
        {
            ShowOnlyModsWithNotifications = false;

            _filters.Remove(FilterType.HasModNotifications);

            ResetContent();
            var settingss = await ReadCharacterSettings();


            settingss.ShowOnlyModsWithNotifications = ShowOnlyModsWithNotifications;

            await SaveCharacterSettings(settingss);

            return;
        }

        _filters[FilterType.HasModNotifications] = new GridFilter(characterGridItem =>
            characterGridItem.Notification);

        ShowOnlyModsWithNotifications = true;

        ResetContent();

        var settings = await ReadCharacterSettings();

        settings.ShowOnlyModsWithNotifications = ShowOnlyModsWithNotifications;

        await SaveCharacterSettings(settings).ConfigureAwait(false);
    }

    [ObservableProperty] private bool _showOnlyWithEnabledMods;

    [RelayCommand]
    private async Task ShowOnlyCharactersWithEnabledMods()
    {
        if (ShowOnlyWithEnabledMods)
        {
            ShowOnlyWithEnabledMods = false;

            _filters.Remove(FilterType.HasEnabledMods);

            ResetContent();

            var settingss = await ReadCharacterSettings();

            settingss.ShowOnlyCharactersWithEnabledMods = ShowOnlyWithEnabledMods;

            await SaveCharacterSettings(settingss);

            return;
        }

        _filters[FilterType.HasEnabledMods] = new GridFilter(characterGridItem => characterGridItem.Mods.Any(m => m.IsEnabled));

        ShowOnlyWithEnabledMods = true;

        ResetContent();

        var settings = await ReadCharacterSettings();

        settings.ShowOnlyCharactersWithEnabledMods = ShowOnlyWithEnabledMods;

        await SaveCharacterSettings(settings).ConfigureAwait(false);
    }


    [ObservableProperty] private string _pinText = DefaultPinText;

    [ObservableProperty] private string _pinGlyph = DefaultPinGlyph;

    const string DefaultPinGlyph = "\uE718";
    const string DefaultPinText = "Pin To Top";
    const string DefaultUnpinGlyph = "\uE77A";
    const string DefaultUnpinText = "Unpin Character";

    private string GetLocalizedPinText() =>
        _localizer.GetLocalizedStringOrDefault("Characters_PinToTopText") ?? DefaultPinText;

    private string GetLocalizedUnpinText() =>
        _localizer.GetLocalizedStringOrDefault("Characters_UnpinCharacterText") ?? DefaultUnpinText;

    public void OnRightClickContext(CharacterGridItemModel clickedCharacter)
    {
        ClearNotificationsCommand.NotifyCanExecuteChanged();
        DisableCharacterModsCommand.NotifyCanExecuteChanged();
        if (clickedCharacter.IsPinned)
        {
            PinText = GetLocalizedUnpinText();
            PinGlyph = DefaultUnpinGlyph;
        }
        else
        {
            PinText = GetLocalizedPinText();
            PinGlyph = DefaultPinGlyph;
        }
    }

    [RelayCommand]
    private async Task PinCharacterAsync(CharacterGridItemModel character)
    {
        if (character.IsPinned)
        {
            character.IsPinned = false;

            ResetContent();

            var settingss = await ReadCharacterSettings();

            var pinedCharacterss = _backendCharacters.Where(ch => ch.IsPinned)
                .Select(ch => ch.Character.InternalName.Id)
                .ToArray();
            settingss.PinedCharacters = pinedCharacterss;
            await SaveCharacterSettings(settingss);
            return;
        }


        character.IsPinned = true;

        ResetContent();

        var settings = await ReadCharacterSettings();

        var pinedCharacters = _backendCharacters
            .Where(ch => ch.IsPinned)
            .Select(ch => ch.Character.InternalName.Id)
            .ToArray();

        settings.PinedCharacters = pinedCharacters;

        await SaveCharacterSettings(settings).ConfigureAwait(false);
    }


    private bool CanClearNotifications(CharacterGridItemModel? character)
    {
        return character?.Notification ?? false;
    }

    [RelayCommand(CanExecute = nameof(CanClearNotifications))]
    private async Task ClearNotificationsAsync(CharacterGridItemModel character)
    {
        await _modNotificationManager.ClearModNotificationsAsync(character.Character.InternalName);
        await RefreshNotificationsAsync().ConfigureAwait(false);
    }

    private bool CanDisableCharacterMods(CharacterGridItemModel? character) =>
        character is not null && character.Mods.Any(m => m.IsEnabled);

    [RelayCommand(CanExecute = nameof(CanDisableCharacterMods))]
    private async Task DisableCharacterMods(CharacterGridItemModel character)
    {
        var updatedMods = new List<CharacterModItem>();
        try
        {
            await Task.Run(async () =>
            {
                var modList = _skinManagerService.GetCharacterModList(character.Character);
                var mods = modList.Mods.ToArray();
                foreach (var modEntry in mods)
                {
                    if (modList.IsModEnabled(modEntry.Mod))
                    {
                        modList.DisableMod(modEntry.Id);
                    }

                    var modSettings = await modEntry.Mod.Settings.TryReadSettingsAsync(true).ConfigureAwait(false);

                    updatedMods.Add(new CharacterModItem(modEntry.Mod.GetDisplayName(), modEntry.IsEnabled, modSettings?.DateAdded ?? default));
                }
            });
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error disabling mods for character {Character}", character.Character.InternalName);
            NotificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Characters_ErrorDisablingModsTitle") ?? "Error disabling mods", e.Message, TimeSpan.FromSeconds(6));
            return;
        }

        character.SetMods(updatedMods);
        await RefreshMultipleModsWarningAsync();
        NotificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Characters_ModsDisabledTitle") ?? "Mods Disabled", string.Format(_localizer.GetLocalizedStringOrDefault("Characters_ModsDisabledMessage") ?? "Alls mods for {0} have been disabled", character.Character.DisplayName), null);
    }

    [RelayCommand]
    private async Task OpenCharacterFolderAsync(CharacterGridItemModel? character)
    {
        if (character is null)
            return;
        var modList = _skinManagerService.GetCharacterModList(character.Character);

        var directoryToOpen = new DirectoryInfo(modList.AbsModsFolderPath);
        if (!directoryToOpen.Exists)
        {
            modList.InstantiateCharacterFolder();
            directoryToOpen.Refresh();

            if (!directoryToOpen.Exists)
            {
                var parentDir = directoryToOpen.Parent;

                if (parentDir is null)
                {
                    _logger.Error("Could not find parent directory of {Directory}", directoryToOpen.FullName);
                    return;
                }

                directoryToOpen = parentDir;
            }
        }

        await Launcher.LaunchFolderAsync(
            await StorageFolder.GetFolderFromPathAsync(directoryToOpen.FullName));
    }

    [RelayCommand]
    private void HideCharacter(CharacterGridItemModel character)
    {
        NotImplemented.Show("Hiding characters is not implemented yet");
    }

    private Task<CharacterOverviewSettings> ReadCharacterSettings() =>
        _localSettingsService
            .ReadOrCreateSettingAsync<CharacterOverviewSettings>(CharacterOverviewSettings.GetKey(_category));

    private Task SaveCharacterSettings(CharacterOverviewSettings settings) =>
        _localSettingsService.SaveSettingAsync(CharacterOverviewSettings.GetKey(_category), settings);


    [RelayCommand]
    private async Task Start3DmigotoAsync() =>
        await SimpleSelectProcessDialogVM.InternalStart(ThreeDMigtoProcessManager,
            SimpleSelectProcessDialogVM.StartType.ModelImporter);


    [RelayCommand]
    private async Task StartGenshinAsync() =>
        await SimpleSelectProcessDialogVM.InternalStart(GenshinProcessManager,
            SimpleSelectProcessDialogVM.StartType.Game);

    /// <summary>
    /// For an XXMI-managed game, opens the XXMI Launcher GUI (no arguments) so the user can
    /// change settings / manage launch through XXMI.
    /// </summary>
    [RelayCommand]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("", "SecurityNoSecurityBrowserCmd2")]
    private async Task OpenXxmiAsync()
    {
        if (!CanOpenXxmi)
            return;
        await LaunchXxmiProcessAsync(XxmiLauncherExePath!, arguments: null, gameLaunch: false);
    }

    /// <summary>
    /// For an XXMI-managed game, launches the game through XXMI the same way XXMI's Quick
    /// Start shortcuts do: <c>XXMI Launcher.exe --nogui --xxmi &lt;GAME&gt;</c>.
    /// </summary>
    [RelayCommand]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("", "SecurityNoSecurityBrowserCmd2")]
    private async Task LaunchXxmiAsync()
    {
        if (!CanLaunchXxmi)
            return;
        await LaunchXxmiProcessAsync(XxmiLauncherExePath!, $"--nogui --xxmi {XxmiGameIdentifier}", gameLaunch: true);
    }

    private readonly SemaphoreSlim _xxmiLaunchLock = new(1, 1);
    private readonly CancellationTokenSource _xxmiPollerCts = new();

    /// <summary>
    /// Whether any relevant process for the current game's XXMI launch is alive. For a game
    /// launched via <c>--nogui</c>, the XXMI Launcher host can exit after handing off to the game,
    /// so we also watch the game's own process name(s) to reflect the true Running state.
    /// </summary>
    private bool IsXxmiOrGameProcessRunning()
    {
        try
        {
            if (System.Diagnostics.Process.GetProcessesByName("XXMI Launcher").Length > 0)
                return true;

            // Per-game process names observed when a game is running under XXMI.
            var names = XxmiGameIdentifier switch
            {
                "GIMI" => new[] { "GenshinImpact", "YuanShen" },
                "SRMI" => new[] { "StarRail" },
                "WWMI" => new[] { "Client-Win64-Shipping" },
                "ZZMI" => new[] { "ZenlessZoneZero" },
                "EFMI" => new[] { "Endfield" },
                _ => Array.Empty<string>()
            };

            foreach (var name in names)
            {
                if (System.Diagnostics.Process.GetProcessesByName(name).Length > 0)
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Starts a background poller that watches for a live 'XXMI Launcher' process so the buttons
    /// reflect the Running state (and re-enable once it exits). Used only when the game is XXMI.
    /// </summary>
    private void StartXxmiProcessPoller()
    {
        if (!IsXxmiManaged)
            return;

        _logger.Debug("XXMI poller starting (game={XxmiGameIdentifier})", XxmiGameIdentifier);
        _ = Task.Run(async () =>
        {
            var token = _xxmiPollerCts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var isRunning = IsXxmiOrGameProcessRunning();
                    if (XxmiIsProcessRunning != isRunning)
                    {
                        _logger.Debug("XXMI process state detected: running={Running}", isRunning);
                        await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
                        {
                            XxmiIsProcessRunning = isRunning;
                            return Task.CompletedTask;
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "XXMI process poller iteration failed");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, _xxmiPollerCts.Token);
    }

    private async Task LaunchXxmiProcessAsync(string exePath, string? arguments, bool gameLaunch)
    {
        // Re-entrancy guard: prevents a double-click from spawning a second XXMI instance while
        // the first is still starting (XXMI may fail when two instances race to init).
        if (IsLaunchingXxmi)
        {
            _logger.Debug("Ignoring XXMI launch, one already in progress");
            return;
        }

        await _xxmiLaunchLock.WaitAsync();
        try
        {
            if (IsLaunchingXxmi)
                return;

            // The game-launch path: after quitting a game, a stale XXMI Launcher host can linger
            // for a moment. Relaunching into a half-dead instance repeats the previous failure, so
            // we clear any remaining XXMI processes first (the user explicitly asked to launch).
            // The Open-XXMI (settings) path never kills anything.
            var running = System.Diagnostics.Process.GetProcessesByName("XXMI Launcher");
            if (running.Length > 0)
            {
                if (gameLaunch)
                {
                    _logger.Information("Clearing {Count} lingering XXMI Launcher process(es) before game launch", running.Length);
                    foreach (var r in running)
                    {
                        try
                        {
                            if (!r.HasExited) r.Kill();
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning(ex, "Could not close stale XXMI Launcher process {Pid}", r.Id);
                        }
                        finally
                        {
                            r.Dispose();
                        }
                    }
                    // Give them a moment to fully exit before we start a fresh instance.
                    await Task.Delay(800);
                }
                else
                {
                    foreach (var r in running) r.Dispose();
                    _logger.Information("XXMI Launcher already running; skipping duplicate launch");
                    NotificationManager.ShowNotification(
                        _localizer.GetLocalizedStringOrDefault("Notification_AlreadyRunningTitle") ?? "Already running",
                        _localizer.GetLocalizedStringOrDefault("Notification_XxmiAlreadyRunning") ?? "XXMI Launcher is already open.",
                        null);
                    return;
                }
            }

            IsLaunchingXxmi = true;
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath),
                    Arguments = arguments ?? string.Empty,
                    UseShellExecute = true
                };

                var started = System.Diagnostics.Process.Start(startInfo);
                _logger.Information("Launched XXMI Launcher: {Path} {Arguments} (pid={Pid})", exePath, arguments, started?.Id);

                // Keep the button disabled during the launch window so a second click can't race
                // a duplicate instance. For the GUI (Open XXMI) the process stays alive; we only
                // wait for the initial startup window (timeout) so the button isn't stuck disabled.
                await WaitForLaunchWindowAsync(started, TimeSpan.FromSeconds(6));
            }
            finally
            {
                IsLaunchingXxmi = false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to launch XXMI Launcher ({Arguments})", arguments);
            NotificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification_CouldNotStartProcess") ?? "Could not start process",
                ex.Message,
                null);
        }
        finally
        {
            _xxmiLaunchLock.Release();
        }
    }

    private static async Task WaitForLaunchWindowAsync(System.Diagnostics.Process? process, TimeSpan maxWait)
    {
        if (process is null)
            return;

        // Wait until the process exits or the window elapses, whichever comes first.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < maxWait && !process.HasExited)
        {
            await Task.Delay(100);
        }
    }

    [RelayCommand]
    private async Task EnableAllModsDialogAsync(Microsoft.UI.Xaml.Controls.ContentDialog dialog)
    {
        dialog.XamlRoot ??= App.MainWindow.Content.XamlRoot;
        dialog.Title = _localizer.GetLocalizedStringOrDefault("CharactersPage_EnableAllDialog_Title") ?? "Enable all mods?";
        dialog.PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("CharactersPage_EnableAllDialog_PrimaryButtonText") ?? "Cancel";
        dialog.SecondaryButtonText = _localizer.GetLocalizedStringOrDefault("CharactersPage_EnableAllDialog_SecondaryButtonText") ?? "Confirm";
        var result = await dialog.ShowAsync();
        if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Secondary)
        {
            var activeCategories = _backendCharacters.Select(c => c.Character.ModCategory).DistinctBy(c => c.InternalName).ToList();
            if (activeCategories.Count == 0) return;

            var errors = await _skinManagerService.EnableAllModsAsync(activeCategories);

            await RefreshBackendCharactersModsAsync();
            ResetContent();

            if (errors.Length == 0)
            {
                var categoryNames = string.Join(", ", activeCategories.Select(c => c.DisplayNamePlural));
                NotificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Characters_ModsEnabledTitle") ?? "Mods enabled", string.Format(_localizer.GetLocalizedStringOrDefault("Characters_ModsEnabledMessage") ?? "All tracked mods have been enabled for {0}.", categoryNames), TimeSpan.FromSeconds(5));
            }
            else
            {
                NotificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Characters_ErrorsEnablingModsTitle") ?? "Errors while enabling mods", string.Format(_localizer.GetLocalizedStringOrDefault("Characters_ErrorsEnablingModsMessage") ?? "An error occurred for {0} mods. Check logs.", errors.Length), TimeSpan.FromSeconds(10));
            }
        }
    }

    [RelayCommand]
    private async Task DisableAllModsDialogAsync(Microsoft.UI.Xaml.Controls.ContentDialog dialog)
    {
        dialog.XamlRoot ??= App.MainWindow.Content.XamlRoot;
        dialog.Title = _localizer.GetLocalizedStringOrDefault("CharactersPage_DisableAllDialog_Title") ?? "Disable all mods?";
        dialog.PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("CharactersPage_DisableAllDialog_PrimaryButtonText") ?? "Cancel";
        dialog.SecondaryButtonText = _localizer.GetLocalizedStringOrDefault("CharactersPage_DisableAllDialog_SecondaryButtonText") ?? "Confirm";
        var result = await dialog.ShowAsync();
        if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Secondary)
        {
            var activeCategories = _backendCharacters.Select(c => c.Character.ModCategory).DistinctBy(c => c.InternalName).ToList();
            if (activeCategories.Count == 0) return;

            var errors = await _skinManagerService.DisableAllModsAsync(activeCategories);

            await RefreshBackendCharactersModsAsync();
            ResetContent();

            if (errors.Length == 0)
            {
                var categoryNames = string.Join(", ", activeCategories.Select(c => c.DisplayNamePlural));
                NotificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Characters_ModsDisabledSuccessTitle") ?? "Mods disabled", string.Format(_localizer.GetLocalizedStringOrDefault("Characters_ModsDisabledSuccessMessage") ?? "All tracked mods have been disabled for {0}.", categoryNames), TimeSpan.FromSeconds(5));
            }
            else
            {
                NotificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Characters_ErrorsDisablingModsTitle") ?? "Errors while disabling mods", string.Format(_localizer.GetLocalizedStringOrDefault("Characters_ErrorsDisablingModsMessage") ?? "An error occurred for {0} mods. Check logs.", errors.Length), TimeSpan.FromSeconds(10));
            }
        }
    }

    [RelayCommand]
    private async Task CleanUpModsDialogAsync(Microsoft.UI.Xaml.Controls.ContentDialog dialog)
    {
        dialog.XamlRoot ??= App.MainWindow.Content.XamlRoot;
        dialog.Title = _localizer.GetLocalizedStringOrDefault("CharactersPage_CleanUpDialog_Title") ?? "Clean up disable mods?";
        dialog.PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("CharactersPage_CleanUpDialog_PrimaryButtonText") ?? "Cancel";
        dialog.SecondaryButtonText = _localizer.GetLocalizedStringOrDefault("CharactersPage_CleanUpDialog_SecondaryButtonText") ?? "Yeah man i wanna do it";
        var result = await dialog.ShowAsync();
        if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Secondary)
        {
            var activeCategories = _backendCharacters.Select(c => c.Character.ModCategory).DistinctBy(c => c.InternalName).ToList();
            if (activeCategories.Count == 0) return;

            var totalDeleted = await _skinManagerService.CleanUpDisabledModsAsync(activeCategories);
            await _skinManagerService.RefreshModsAsync();

            await RefreshBackendCharactersModsAsync();
            ResetContent();

            var categoryNames = string.Join(", ", activeCategories.Select(c => c.DisplayNamePlural));
            NotificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Characters_CleanupCompleteTitle") ?? "Cleanup complete", string.Format(_localizer.GetLocalizedStringOrDefault("Characters_CleanupCompleteMessage") ?? "Deleted {0} disabled mods for {1}.", totalDeleted, categoryNames), TimeSpan.FromSeconds(5));
        }
    }

    private async Task RefreshBackendCharactersModsAsync()
    {
        foreach (var characterGridItemModel in _backendCharacters)
        {
            var modList = _skinManagerService.GetCharacterModList(characterGridItemModel.Character);

            var characterModItems = new List<CharacterModItem>();
            foreach (var skinModEntry in modList.Mods)
            {
                var modSettings = await skinModEntry.Mod.Settings.TryReadSettingsAsync(true);
                characterModItems.Add(new CharacterModItem(skinModEntry.Mod.GetDisplayName(), skinModEntry.IsEnabled, modSettings?.DateAdded ?? default));
            }

            characterGridItemModel.SetMods(characterModItems);
        }
        await RefreshMultipleModsWarningAsync();
    }

    [ObservableProperty] private bool _isAddingMod = false;

    public async Task ModDroppedOnCharacterAsync(CharacterGridItemModel characterGridItemModel,
        IReadOnlyList<IStorageItem> storageItems)
    {
        if (IsAddingMod)
        {
            _logger.Warning("Already adding mod");
            return;
        }

        var modList =
            _skinManagerService.CharacterModLists.FirstOrDefault(x =>
                x.Character.InternalNameEquals(characterGridItemModel.Character));
        if (modList is null)
        {
            _logger.Warning("No mod list found for character {Character}",
                characterGridItemModel.Character.InternalName);
            return;
        }

        try
        {
            IsAddingMod = true;
            await _modDragAndDropService.AddStorageItemFoldersAsync(modList, storageItems);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error adding mod");
            NotificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Characters_ErrorAddingModTitle") ?? "Error adding mod", e.Message, TimeSpan.FromSeconds(10));
        }
        finally
        {
            IsAddingMod = false;
        }
    }

    public async Task ModUrlDroppedOnCharacterAsync(CharacterGridItemModel characterGridItemModel, Uri uri)
    {
        if (IsAddingMod)
        {
            _logger.Warning("Already adding mod");
            return;
        }

        var modList = _skinManagerService.CharacterModLists.FirstOrDefault(x => x.Character.InternalNameEquals(characterGridItemModel.Character));
        if (modList is null)
        {
            _logger.Warning("No mod list found for character {Character}",
                characterGridItemModel.Character.InternalName);
            return;
        }

        if (!GameBananaUrlHelper.TryGetModIdFromUrl(uri, out _))
        {
            NotificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Characters_InvalidGameBananaLinkTitle") ?? "Invalid GameBanana mod page link", "", null);
            return;
        }

        try
        {
            IsAddingMod = true;
            await _modDragAndDropService.AddModFromUrlAsync(modList, uri);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error opening mod page window");
            NotificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("Characters_ErrorOpeningModPageTitle") ?? "Error opening mod page window", e.Message, TimeSpan.FromSeconds(10));
        }
        finally
        {
            IsAddingMod = false;
        }
    }


    private CharacterGridItemModel? FindCharacterByInternalName(string internalName)
    {
        return _backendCharacters.FirstOrDefault(x =>
            x.Character.InternalNameEquals(internalName));
    }


    [RelayCommand]
    private async Task SortBy(IEnumerable<GridItemSortingMethod> methodTypes)
    {
        if (_isNavigating) return;
        var sortingMethodType = methodTypes.First();

        ResetContent();

        var settings = await ReadCharacterSettings();
        settings.SortingMethod = sortingMethodType.SortingMethodType;
        await SaveCharacterSettings(settings).ConfigureAwait(false);
    }


    [RelayCommand]
    private async Task InvertSorting()
    {
        ResetContent();

        var settings = await ReadCharacterSettings();
        settings.SortByDescending = SortByDescending;
        await SaveCharacterSettings(settings).ConfigureAwait(false);
    }

    [RelayCommand]
    private void CheckForUpdatesForCharacter(object? characterGridItemModel)
    {
        if (characterGridItemModel is not CharacterGridItemModel character)
            return;

        var modList = _skinManagerService.GetCharacterModList(character.Character);
        if (modList is null)
        {
            _logger.Warning("No mod list found for character {Character}", character.Character.InternalName);
            return;
        }

        var check = ModCheckRequest.ForCharacter(character.Character);

        _modUpdateAvailableChecker.CheckNow(check.WithIgnoreLastChecked());
    }


    private void InitializeSorters()
    {
        var lastCharacters = new List<CharacterGridItemModel>
        {
            FindCharacterByInternalName(_gameService.GlidersCharacterInternalName)!,
            FindCharacterByInternalName(_gameService.WeaponsCharacterInternalName)!
        };

        var othersCharacter = _backendCharacters.FirstOrDefault(ch =>
            ch.Character.InternalName.Id.Contains("Others", StringComparison.OrdinalIgnoreCase));

        var alphabetical = new GridItemSortingMethod(GridItemSorter.Alphabetical, _localizer.GetLocalizedStringOrDefault("CharactersPage_SortAlphabetical_Name") ?? "Alphabetical", othersCharacter, lastCharacters);
        SortingMethods.Add(alphabetical);

        var byModCount = new GridItemSortingMethod(GridItemSorter.ModCount, _localizer.GetLocalizedStringOrDefault("CharactersPage_SortModCount_Name") ?? "Mod Count", othersCharacter, lastCharacters);
        SortingMethods.Add(byModCount);


        var byModRecentlyAdded = new GridItemSortingMethod(GridItemSorter.ModRecentlyAdded, _localizer.GetLocalizedStringOrDefault("CharactersPage_SortRecentlyAdded_Name") ?? "Recently Added", othersCharacter, lastCharacters);
        SortingMethods.Add(byModRecentlyAdded);

        if (_category.ModCategory == ModCategory.Character)
        {
            SortingMethods.Add(new GridItemSortingMethod(GridItemSorter.ReleaseDate, _localizer.GetLocalizedStringOrDefault("CharactersPage_SortReleaseDate_Name") ?? "Release Date", othersCharacter, lastCharacters));
            SortingMethods.Add(new GridItemSortingMethod(GridItemSorter.Rarity, _localizer.GetLocalizedStringOrDefault("CharactersPage_SortRarity_Name") ?? "Rarity", othersCharacter, lastCharacters));
        }

        if (_category.ModCategory == ModCategory.Weapons)
        {
            SortingMethods.Add(new GridItemSortingMethod(GridItemSorter.Rarity, _localizer.GetLocalizedStringOrDefault("CharactersPage_SortRarity_Name") ?? "Rarity", othersCharacter, lastCharacters));
        }
    }


    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task ApplyPreset(object? modPresetObject)
    {
        if (modPresetObject is not CharactersViewModels.ModPresetEntryVm modPresetEntryVm)
            return;

        var presetName = modPresetEntryVm.Name;

        using var _ = _busyService.SetPageBusy(this);

        var result = await Task.Run(() => _modPresetHandlerService.ApplyModPresetAsync(presetName));
        await RefreshMultipleModsWarningAsync();
        ResetContent();

        if (result.Notification is not null)
            NotificationManager.ShowNotification(result.Notification);
    }


    private void OnBusyChangedHandler(object? sender, BusyChangedEventArgs args)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            if (args.IsKey(this))
                IsBusy = args.IsBusy;
        });
    }

    public sealed class GridItemSortingMethod(
        Sorter<CharacterGridItemModel> sortingMethodType,
        string localizedDisplayName,
        CharacterGridItemModel? firstItem = null,
        ICollection<CharacterGridItemModel>? lastItems = null)
        : SortingMethod<CharacterGridItemModel>(sortingMethodType, firstItem, lastItems)
    {
        public override string ToString() => localizedDisplayName;
        protected override void PostSortAction(List<CharacterGridItemModel> sortedList)
        {
            var pinnedCharacters = sortedList.Where(x => x.IsPinned).ToArray();
            if (pinnedCharacters.Length == 0)
                return;
            var pinnedStartIndex = FirstItem is not null ? 1 : 0;
            foreach (var pinnedCharacter in pinnedCharacters)
            {
                if (pinnedCharacter == FirstItem) continue;
                sortedList.Remove(pinnedCharacter);
                sortedList.Insert(pinnedStartIndex, pinnedCharacter);
                pinnedStartIndex++;
            }
        }
    }

    public sealed class GridItemSorter : Sorter<CharacterGridItemModel>
    {
        private GridItemSorter(string sortingMethodType, SortFunc firstSortFunc, AdditionalSortFunc? secondSortFunc = null,
            AdditionalSortFunc? thirdSortFunc = null) : base(sortingMethodType, firstSortFunc, secondSortFunc, thirdSortFunc)
        {
        }

        public const string AlphabeticalSortName = "Alphabetical";

        public static GridItemSorter Alphabetical { get; } =
            new(
                AlphabeticalSortName,
                (characters, isDescending) =>
                    isDescending
                        ? characters.OrderByDescending(x => x.Character.DisplayName)
                        : characters.OrderBy(x => x.Character.DisplayName
                        ));


        public const string ReleaseDateSortName = "Release Date";

        public static GridItemSorter ReleaseDate { get; } =
            new(
                ReleaseDateSortName,
                (characters, isDescending) =>
                    !isDescending
                        ? characters.OrderByDescending(x => ((ICharacter)x.Character).ReleaseDate)
                        : characters.OrderBy(x => ((ICharacter)x.Character).ReleaseDate),
                (characters, _) =>
                    characters.ThenBy(x => x.Character.DisplayName
                    ));


        public const string RaritySortName = "Rarity";

        public static GridItemSorter Rarity { get; } =
            new(
                RaritySortName,
                (characters, isDescending) =>
                    !isDescending
                        ? characters.OrderByDescending(x => ((IRarity)x.Character).Rarity)
                        : characters.OrderBy(x => ((IRarity)x.Character).Rarity),
                (characters, _) =>
                    characters.ThenBy(x => x.Character.DisplayName
                    ));


        public const string ModCountSortName = "Mod Count";

        public static GridItemSorter ModCount { get; } =
            new(
                ModCountSortName,
                (characters, isDescending) =>
                    !isDescending
                        ? characters.OrderByDescending(x => x.ModCount)
                        : characters.OrderBy(x => x.ModCount),
                (characters, _) =>
                    characters.ThenBy(x => x.Character.DisplayName
                    ));


        public const string ModRecentlyAddedName = "Recently Added Mods";

        public static GridItemSorter ModRecentlyAdded { get; } =
            new(
                ModRecentlyAddedName,
                (characters, isDescending) =>
                    !isDescending
                        ? characters.OrderByDescending(x =>
                        {
                            var validDates = x.Mods.Where(mod => mod.DateAdded != default).Select(mod => mod.DateAdded)
                                .ToArray();
                            if (validDates.Any())
                                return validDates.Max();
                            else
                                return DateTime.MinValue;
                        })
                        : characters.OrderBy(x =>
                        {
                            var validDates = x.Mods.Where(mod => mod.DateAdded != default).Select(mod => mod.DateAdded)
                                .ToArray();
                            if (validDates.Any())
                                return validDates.Min();
                            else
                                return DateTime.MaxValue;
                        }),
                (characters, _) =>
                    characters.ThenBy(x => x.Character.DisplayName
                    ));
    }

    [RelayCommand]
    private Task RandomizeMods() => _modRandomizationService.ShowRandomizeModsDialog();
}

public sealed class GridFilter
{
    private readonly Func<CharacterGridItemModel, bool> _filter;

    public GridFilter(Func<CharacterGridItemModel, bool> filter)
    {
        _filter = filter;
    }

    public bool Filter(CharacterGridItemModel character)
    {
        return _filter(character);
    }

    public IEnumerable<CharacterGridItemModel> Filter(IEnumerable<CharacterGridItemModel> characters)
    {
        return characters.Where(Filter);
    }
}

public enum FilterType
{
    Element,
    Search,
    HasMods,
    HasModNotifications,
    HasEnabledMods
}

public class ScrollToCharacterArgs : EventArgs
{
    public CharacterGridItemModel Character { get; }

    public ScrollToCharacterArgs(CharacterGridItemModel character)
    {
        Character = character;
    }
}