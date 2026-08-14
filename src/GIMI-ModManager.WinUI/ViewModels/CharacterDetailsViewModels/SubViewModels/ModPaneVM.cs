using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkitWrapper;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Entities;
using GIMI_ModManager.Core.Entities.Mods.Contract;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.WinUI.Services;
using GIMI_ModManager.WinUI.Services.ModHandling;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.Views.Controls;
using Microsoft.UI.Dispatching;
using Serilog;

namespace GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;

public sealed partial class ModPaneVM(
    ISkinManagerService skinManagerService,
    NotificationManager notificationService,
    ModSettingsService modSettingsService,
    ImageHandlerService imageHandlerService,
    GIMI_ModManager.Core.GamesService.IGameService gameService,
    GameBananaService gameBananaService,
    ModInstallerService modInstallerService,
    GIMI_ModManager.Core.Services.GameBanana.GameBananaCoreService gameBananaCoreService,
    GIMI_ModManager.Core.Services.ArchiveService archiveService)
    : ObservableRecipient, IRecipient<ModChangedMessage>
{
    private readonly ILogger _logger = Log.ForContext<ModPaneVM>();
    private readonly ISkinManagerService _skinManagerService = skinManagerService;
    private readonly NotificationManager _notificationService = notificationService;
    private readonly ModSettingsService _modSettingsService = modSettingsService;
    private readonly ImageHandlerService _imageHandlerService = imageHandlerService;
    private readonly GIMI_ModManager.Core.GamesService.IGameService _gameService = gameService;
    private readonly GameBananaService _gameBananaService = gameBananaService;
    private readonly ModInstallerService _modInstallerService = modInstallerService;
    private readonly GIMI_ModManager.Core.Services.GameBanana.GameBananaCoreService _gameBananaCoreService = gameBananaCoreService;
    private readonly GIMI_ModManager.Core.Services.ArchiveService _archiveService = archiveService;

    private readonly AsyncLock _loadModLock = new();
    private CancellationToken _cancellationToken = new();
    private DispatcherQueue _dispatcherQueue = null!;
    public bool IsInitialized { get; private set; }
    public BusySetter BusySetter { get; set; } = null!;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotReadOnly))]
    private bool _isReadOnly = true;

    [ObservableProperty] private bool _isEditingModName;

    public bool IsNotReadOnly => !IsReadOnly;

    private Guid? _loadedModId;
    private CharacterSkinEntry? _loadedMod;

    [MemberNotNullWhen(true, nameof(_loadedModId), nameof(_loadedMod))]
    public bool IsModLoaded => _loadedModId != null && ModModel.IsLoaded && _loadedMod != null;

    [ObservableProperty] private ModPaneFieldsVm _modModel = new();


    public bool QueueLoadMod(Guid? modId, bool force = false) => _channel.Writer.TryWrite(new LoadModMessage { ModId = modId, Force = force });


    private readonly Channel<LoadModMessage> _channel = Channel.CreateBounded<LoadModMessage>(
        new BoundedChannelOptions(1)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    private async Task ModLoaderLoopAsync()
    {
        // Runs on the UI thread
        await foreach (var loadModMessage in _channel.Reader.ReadAllAsync(CancellationToken.None))
        {
            if (_cancellationToken.IsCancellationRequested)
                break;
            using var _ = await LockAsync().ConfigureAwait(false);
            IsReadOnly = true;
            IsEditingModName = false;
            try
            {
                if (loadModMessage.ModId is null)
                {
                    await UnloadModAsync();
                    NotifyAllCommands();
                    OnPropertyChanged(nameof(IsModLoaded));
                    OnPropertyChanged(nameof(CanSearchModUrl));
                    OnPropertyChanged(nameof(CanOpenModUrl));
                    OnPropertyChanged(nameof(CanOpenModUrlLink));
                    OnPropertyChanged(nameof(CanRefetchModUrl));
                    continue;
                }

                await LoadModAsync(loadModMessage.ModId.Value, loadModMessage.Force);
                NotifyAllCommands();
                OnPropertyChanged(nameof(IsModLoaded));
                OnPropertyChanged(nameof(CanSearchModUrl));
                OnPropertyChanged(nameof(CanOpenModUrl));
                OnPropertyChanged(nameof(CanOpenModUrlLink));
                OnPropertyChanged(nameof(CanRefetchModUrl));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _notificationService.ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("CharDetails_ErrorLoadingMod") ?? "Error loading mod", e.Message, null);
            }
        }
    }

    private async Task LoadModAsync(Guid modId, bool force)
    {
        if (modId == _loadedModId && force == false)
            return;

        var modPaneData = await Task.Run(async () =>
        {
            var modEntry = _skinManagerService.GetModEntryById(modId);
            if (modEntry == null)
                return null;

            var mod = modEntry.Mod;

            var modSettings =
                await mod.Settings.TryReadSettingsAsync(useCache: false, cancellationToken: _cancellationToken);

            if (modSettings is null)
                return null;

            ICollection<KeySwapSection>? keySwaps = null;
            try
            {
                if (mod.KeySwaps is not null)
                    keySwaps = (await mod.KeySwaps.ReadKeySwapConfiguration(_cancellationToken)).ToArray();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                _notificationService.ShowNotification(string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("CharDetails_FailedLoadKeySwaps") ?? "Failed to load keyswaps for mod {0}", mod.GetDisplayName()), e.Message, null);
            }

            return new { modEntry, modSettings, keySwaps };
        }, _cancellationToken);

        if (modPaneData is null)
            return;


        _loadedMod = modPaneData.modEntry;
        ModModel = ModPaneFieldsVm.FromModEntry(modPaneData.modEntry, modPaneData.modSettings, modPaneData.keySwaps ?? []);
        ModModel.PropertyChanged += ModModel_PropertyChanged;
        _loadedModId = modId;
        IsReadOnly = false;
    }

    private void ModModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SaveModSettingsCommand.NotifyCanExecuteChanged();
        if (e.PropertyName is nameof(ModPaneFieldsVm.ModUrl))
            NotifyModUrlChanged();
    }
    private void BusySetter_HardBusyChanged(object? sender, EventArgs eventArgs) => NotifyAllCommands();

    private Task UnloadModAsync()
    {
        _loadedModId = null;
        _loadedMod = null;
        if (ModModel.IsLoaded)
            ModModel.PropertyChanged -= ModModel_PropertyChanged;
        ModModel = new ModPaneFieldsVm();
        return Task.CompletedTask;
    }


    private readonly record struct LoadModMessage
    {
        public Guid? ModId { get; init; }
        public bool Force { get; init; }
    }

    public void Receive(ModChangedMessage message)
    {
        if (!IsModLoaded)
            return;

        if (message.SkinEntry.Id != _loadedModId)
            return;

        if (message.sender == this)
            return;

        QueueLoadMod(message.SkinEntry.Id, true);
    }

    public Task OnNavigatedToAsync(DispatcherQueue dispatcherQueue, CancellationToken navigationCt)
    {
        _dispatcherQueue = dispatcherQueue;
        _cancellationToken = navigationCt;
        GIMI_ModManager.WinUI.Helpers.AltKeyTracker.Changed += AltKeyTracker_OnChanged;
        IsRefetchActive = GIMI_ModManager.WinUI.Helpers.AltKeyTracker.IsActive;
        _ = _dispatcherQueue.EnqueueAsync(ModLoaderLoopAsync);
        Messenger.RegisterAll(this);
        BusySetter.HardBusyChanged += BusySetter_HardBusyChanged;
        IsInitialized = true;
        return Task.CompletedTask;
    }

    private void AltKeyTracker_OnChanged(object? sender, EventArgs e)
    {
        _dispatcherQueue.TryEnqueue(() => IsRefetchActive = GIMI_ModManager.WinUI.Helpers.AltKeyTracker.IsActive);
    }

    /// <summary>True while Alt is held (advanced mode).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefetchModUrl))]
    [NotifyPropertyChangedFor(nameof(CanOpenModUrlLink))]
    [NotifyCanExecuteChangedFor(nameof(RefetchModUrlCommand))]
    private bool _isRefetchActive;

    /// <summary>Refetch (Alt held + a URL set) is available for the loaded mod.</summary>
    public bool CanRefetchModUrl => IsModLoaded && !string.IsNullOrWhiteSpace(ModModel.ModUrl) && IsRefetchActive;

    /// <summary>Open-url link shows when a URL is set and Alt is not held.</summary>
    public bool CanOpenModUrlLink => IsModLoaded && !string.IsNullOrWhiteSpace(ModModel.ModUrl) && !IsRefetchActive;

    [RelayCommand(CanExecute = nameof(CanRefetchModUrl))]
    private async Task RefetchModUrlAsync()
    {
        if (!IsModLoaded || !Uri.TryCreate(ModModel.ModUrl, UriKind.Absolute, out var url))
            return;

        try
        {
            var monitor = await _modInstallerService.StartModInstallationAsync(
                new DirectoryInfo(_loadedMod!.Mod.FullPath), _loadedMod.ModList, inGameSkin: null,
                setup: options =>
                {
                    options.ModUrl = url;
                    options.AssociateOnly = true;
                });
            await monitor.Task;

            _loadedMod.Mod.ClearCache();
            Messenger.Send(new ModChangedMessage(this, _loadedMod, null));
            QueueLoadMod(_loadedModId, true);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Refetch mod info failed");
            _notificationService.ShowNotification(
                App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("ModPane_SearchError") ?? "Search failed",
                e.Message, null);
        }
    }

    public void OnNavigatedFrom()
    {
        GIMI_ModManager.WinUI.Helpers.AltKeyTracker.Changed -= AltKeyTracker_OnChanged;
        _channel.Writer.TryComplete();
        Messenger.UnregisterAll(this);
        try
        {
            _loadModLock.Dispose();
        }
        catch (Exception e)
        {
            _logger.Warning(e, "Failed to dispose of load mod lock");
        }
    }

    private bool DefaultCanExecute => IsModLoaded && IsNotReadOnly && BusySetter.IsNotHardBusy;

    #region Commands

    private bool CanSetModIniFile() => DefaultCanExecute;

    [RelayCommand(CanExecute = nameof(CanSetModIniFile))]
    private async Task SetModIniFileAsync()
    {
        if (!IsModLoaded) return;
        try
        {
            var modFolderPath = _loadedMod.Mod.FullPath;

            var dataPackage = new DataPackage();
            dataPackage.SetText(modFolderPath);
            Clipboard.SetContent(dataPackage);

            _notificationService.ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("CharDetails_ModFolderCopied") ?? "Mod folder path copied to clipboard", "", TimeSpan.FromSeconds(3));
        }
        catch (Exception e)
        {
            _logger.Error(e, "An error occured while trying to copy mod folder path to clipboard when setting .ini");
        }


        var filePicker = new FileOpenPicker();
        filePicker.SettingsIdentifier = "IniFilerPicker";
        filePicker.FileTypeFilter.Add(".ini");
        filePicker.CommitButtonText = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("CharDetails_SetButton.Text") ?? "Set";
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);
        var file = await filePicker.PickSingleFileAsync();

        if (file is null)
        {
            _logger.Debug("User cancelled file picker.");
            return;
        }

        var result = await Task.Run(() => _modSettingsService.SetModIniAsync(_loadedMod.Id, file.Path));


        if (result.Notification is not null)
            _notificationService.ShowNotification(result.Notification);

        _loadedMod.Mod.ClearCache();

        Messenger.Send(new ModChangedMessage(this, _loadedMod, null));
        QueueLoadMod(_loadedModId, true);
    }

    private bool CanClearSetModIniFile() => DefaultCanExecute;

    [RelayCommand(CanExecute = nameof(CanClearSetModIniFile))]
    private async Task ClearSetModIniFileAsync()
    {
        await CommandWrapper(async () =>
        {
            if (!IsModLoaded) return;

            var autoDetect = ModModel.IgnoreMergedIni;
            var result = await Task.Run(() =>
                _modSettingsService.SetModIniAsync(_loadedModId.Value, string.Empty, autoDetect), _cancellationToken);


            if (result.Notification is not null)
                _notificationService.ShowNotification(result.Notification);

            _loadedMod.Mod.ClearCache();

            Messenger.Send(new ModChangedMessage(this, _loadedMod, null));
            QueueLoadMod(_loadedModId, true);
        }).ConfigureAwait(false);
    }

    private bool CanPickImageUri() => DefaultCanExecute;

    [RelayCommand(CanExecute = nameof(CanPickImageUri))]
    private async Task PickImageUriAsync()
    {
        if (!IsModLoaded) return;
        var filePicker = new FileOpenPicker();
        filePicker.CommitButtonText = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("CharDetails_SetImageButton.Text") ?? "Set Image";
        filePicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        filePicker.SettingsIdentifier = "ImagePicker";
        foreach (var supportedImageExtension in Constants.SupportedImageExtensions)
            filePicker.FileTypeFilter.Add(supportedImageExtension);


        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);

        var file = await filePicker.PickSingleFileAsync();

        if (file == null) return;
        var imageUri = new Uri(file.Path);
        ModModel.ImageUri = imageUri;
    }

    private bool CanPasteImage() => DefaultCanExecute;

    [RelayCommand(CanExecute = nameof(CanPasteImage))]
    private async Task PasteImageFromClipboardAsync()
    {
        await CommandWrapper(async () =>
        {
            if (!IsModLoaded) return;

            var clipboardHasValidImageResult = await _imageHandlerService.ClipboardContainsImageAsync();

            if (!clipboardHasValidImageResult.Result)
            {
                _notificationService.ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("CharDetails_ClipboardNoImage") ?? "Clipboard does not contain a valid image", "", null);
                return;
            }

            var imagePath = await _imageHandlerService.GetImageFromClipboardAsync(clipboardHasValidImageResult.DataPackage);

            if (imagePath == null)
            {
                _notificationService.ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("CharDetails_CouldNotGetClipboardImage") ?? "Could not retrieve image from clipboard", "", null);
                return;
            }

            ModModel.ImageUri = imagePath;
        }).ConfigureAwait(false);
    }

    private bool CanCopyImageToClipboard() => DefaultCanExecute;

    [RelayCommand(CanExecute = nameof(CanCopyImageToClipboard))]
    private async Task CopyImageToClipboardAsync()
    {
        await CommandWrapper(async () =>
        {
            if (!File.Exists(ModModel.ImageUri.LocalPath))
                return;

            var file = await StorageFile.GetFileFromPathAsync(ModModel.ImageUri.LocalPath);
            if (file is null)
                return;


            await ImageHandlerService.CopyImageToClipboardAsync(file).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }


    private bool CanClearImage() => DefaultCanExecute && ModModel.ImageUri != ImageHandlerService.StaticPlaceholderImageUri;

    [RelayCommand(CanExecute = nameof(CanClearImage))]
    private void ClearImage()
    {
        if (!IsModLoaded) return;
        ModModel.ImageUri = ImageHandlerService.StaticPlaceholderImageUri;
    }

    private bool CanSaveModSettings() => DefaultCanExecute && ModModel.AnyChanges;

    [RelayCommand(CanExecute = nameof(CanSaveModSettings))]
    private async Task SaveModSettingsAsync()
    {
        await CommandWrapper(async () =>
        {
            if (!CanSaveModSettings()) return;

            var existingModSettings = await _loadedMod!.Mod.Settings.ReadSettingsAsync();

            var updateRequest = new UpdateSettingsRequest();

            if (ModModel.IsImageUriChanged)
                updateRequest.SetImagePath = ModModel.ImageUri;

            if (ModModel.IsModDisplayNameChanged)
                updateRequest.SetCustomName = ModModel.ModDisplayName;

            if (ModModel.IsModUrlChanged)
                updateRequest.SetModUrl = Uri.TryCreate(ModModel.ModUrl, UriKind.Absolute, out var url) ? url : null;

            Result<ModSettings>? result = null;
            Exception? savingKeySwapException = null;

            await Task.Run(async () =>
            {
                if (updateRequest.AnyUpdates)
                {
                    result = await _modSettingsService.SaveSettingsAsync(_loadedModId.Value, updateRequest).ConfigureAwait(false);
                }

                if (!_loadedMod.Mod.Settings.HasMergedIni && !ModModel.KeySwaps.Any()
                    || _loadedMod.Mod.KeySwaps is null ||
                    !ModModel.IsKeySwapsChanged ||
                    existingModSettings.IgnoreMergedIni)
                    return;

                // TODO: Will need to redo keyswap handling at some point doing a quick solution here
                var keySwapSections = new List<KeySwapSection>();

                foreach (var modModelSkinModKeySwap in ModModel.KeySwaps)
                {
                    var variants = int.TryParse(modModelSkinModKeySwap.VariationsCount, out var variantsCount)
                        ? variantsCount
                        : -1;

                    var keySwapSection = new KeySwapSection()
                    {
                        SectionName = modModelSkinModKeySwap.SectionKey,
                        ForwardKey = modModelSkinModKeySwap.ForwardHotkey,
                        BackwardKey = modModelSkinModKeySwap.BackwardHotkey,
                        Variants = variants == -1 ? null : variants,
                        Type = modModelSkinModKeySwap.Type ?? App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("ModPane_Unknown") ?? "Unknown"
                    };

                    keySwapSections.Add(keySwapSection);
                }

                try
                {
                    await _loadedMod.Mod.KeySwaps.SaveKeySwapConfiguration(keySwapSections).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    savingKeySwapException = e;
                    _logger.Error(e, "An error occured trying to save keyswaps for mod {ModPath}", _loadedMod.Mod.FullPath);
                }
            });

            if (result?.Notification is not null && savingKeySwapException is null)
                _notificationService.ShowNotification(result.Notification);

            if (savingKeySwapException is not null)
                _notificationService.ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("CharDetails_FailedSaveKeySwaps") ?? "Failed to save key swaps", savingKeySwapException.Message, null);

            _cancellationToken.ThrowIfCancellationRequested();


            Messenger.Send(new ModChangedMessage(this, _loadedMod, null));
            QueueLoadMod(_loadedModId, true);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether the mod-url field is empty and the loaded mod is editable, so the GameBanana
    /// "search to re-link" button should be shown.
    /// </summary>
    public bool CanSearchModUrl => IsModLoaded && IsNotReadOnly && string.IsNullOrWhiteSpace(ModModel.ModUrl);

    /// <summary>Inverse of <see cref="CanSearchModUrl"/>: true when a URL is set (show the open-link icon).</summary>
    public bool CanOpenModUrl => IsModLoaded && !string.IsNullOrWhiteSpace(ModModel.ModUrl);

    /// <summary>Tooltip for the GameBanana search button.</summary>
    public string SearchTooltip =>
        App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("ModPane_SearchButtonTooltip") ??
        "Search GameBanana for this mod to re-link its URL";

    /// <summary>Tooltip for the Alt + URL refetch button.</summary>
    public string RefetchTooltip =>
        App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("ModPane_RefetchTooltip") ??
        "Refetch mod info from GameBanana";

    /// <summary>Notifies the URL-dependent commands when <see cref="ModPaneFieldsVm.ModUrl"/> changes.</summary>
    private void NotifyModUrlChanged()
    {
        SearchModUrlCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSearchModUrl));
        OnPropertyChanged(nameof(CanOpenModUrl));
        OnPropertyChanged(nameof(CanOpenModUrlLink));
        OnPropertyChanged(nameof(CanRefetchModUrl));
        RefetchModUrlCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSearchModUrl))]
    private async Task SearchModUrlAsync()
    {
        if (!IsModLoaded)
            return;

        try
        {
            // Likely search terms: the mod folder name (with separators/version noise removed), plus
            // the current character name, scoped to the current game on GameBanana.
            var terms = BuildSearchTerms();
            var gameUrl = _gameService.GameBananaUrl;
            var gameRowId = GetGameRowId(gameUrl);

            // Non-character sections (Others/custom) may host their mods as GameBanana Tools.
            var includeTools = GameBananaService.ShouldIncludeTools(_loadedMod?.ModList.Character);
            var results = await _gameBananaService.SearchModsAsync(terms, gameRowId, includeTools, _cancellationToken)
                .ConfigureAwait(true);

            // Always open the dialog (even with no results) so the user can edit the search terms
            // and re-run when the auto-detected terms missed the mod.
            var selected = await ShowSearchDialogAsync(results, terms, gameRowId);
            if (selected is null)
                return;

            await SaveReassignedModUrlAsync(selected.Value.Url, selected.Value.AlwaysRedownload);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error searching GameBanana for mod url");
            _notificationService.ShowNotification(
                App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("ModPane_SearchError") ?? "Search failed",
                e.Message, null);
        }
    }

    private async Task SaveReassignedModUrlAsync(string modUrl, bool alwaysRedownload = false)
    {
        try
        {
            if (!IsModLoaded || !Uri.TryCreate(modUrl, UriKind.Absolute, out var url))
                return;

            var modFolder = new DirectoryInfo(_loadedMod!.Mod.FullPath);
            var modList = _loadedMod.ModList;

            var modInfo = await _gameBananaService.GetModInfoAsync(url, _cancellationToken).ConfigureAwait(true);

            // Decide the flow: if GameBanana has a newer file than what's on disk (or the user asked
            // to always redownload), run the regular download -> install flow (like adding a new mod).
            // Otherwise just associate the URL.
            var updateAvailable = alwaysRedownload || IsGameBananaUpdateAvailable(modInfo, modFolder.FullName);

            InstallMonitor monitor;
            if (updateAvailable)
            {
                _notificationService.ShowNotification(
                    App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("ModPane_DownloadingMod") ?? "Downloading mod",
                    App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("ModPane_DownloadingModMsg") ?? "An update is available, downloading the mod from GameBanana...",
                    TimeSpan.FromSeconds(5));

                var file = modInfo.Files.OrderByDescending(f => f.DateAdded).FirstOrDefault();
                if (file is null)
                    throw new InvalidOperationException("The mod has no downloadable files.");

                var identifier = new GIMI_ModManager.Core.Services.GameBanana.Models.GbModFileIdentifier(
                    new GIMI_ModManager.Core.Services.GameBanana.Models.GbModId(modInfo.ModId),
                    new GIMI_ModManager.Core.Services.GameBanana.Models.GbModFileId(file.FileId));

                var archivePath = await Task.Run(
                    () => _gameBananaCoreService.DownloadModAsync(identifier, ct: _cancellationToken), _cancellationToken);

                var extractedRoot = _archiveService.ExtractArchive(archivePath, App.GetUniqueTmpFolder().FullName);
                var archiveNameSections =
                    Path.GetFileName(extractedRoot.Name).Split(GIMI_ModManager.Core.Services.GameBanana.ModArchiveRepository.Separator);
                var modFolderName = archiveNameSections[0];
                var modFolderExt = Path.GetExtension(extractedRoot.Name);
                var zipRoot = Directory.CreateDirectory(Path.Combine(extractedRoot.Parent!.FullName, "ArchiveRoot"));
                extractedRoot.MoveTo(Path.Combine(zipRoot.FullName, $"{modFolderName}{modFolderExt}"));

                // Regular install flow, but tell the installer about the existing (old) mod so the
                // user gets the overwrite/override option instead of a fresh install.
                monitor = await _modInstallerService.StartModInstallationAsync(zipRoot, modList, inGameSkin: null,
                    setup: options =>
                    {
                        options.ModUrl = url;
                        options.ExistingModToOverwritePath = _loadedMod!.Mod.FullPath;
                    });
            }
            else
            {
                // No update: associate mode — the installer only writes the metadata to the mod's
                // settings file (URL, name, author, description, image); no file changes.
                monitor = await _modInstallerService.StartModInstallationAsync(modFolder, modList, inGameSkin: null,
                    setup: options =>
                    {
                        options.ModUrl = url;
                        options.AssociateOnly = true;
                    });
            }

            // Refresh the mod pane when the installer closes.
            _ = monitor.Task.ContinueWith(_ =>
                App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
                {
                    _loadedMod?.Mod?.ClearCache();
                    if (_loadedModId is not null)
                        QueueLoadMod(_loadedModId.Value, true);
                    return Task.CompletedTask;
                }), TaskScheduler.Default);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error re-linking mod from GameBanana");
            _notificationService.ShowNotification(
                App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("ModPane_SearchError") ?? "Search failed",
                e.Message, null);
        }
    }

    /// <summary>
    /// Compares the GameBanana mod's newest file <see cref="ModFileInfo.DateAdded"/> against the
    /// newest on-disk file timestamp in the installed mod folder (the JSON DateAdded is not used;
    /// it was poisoned by the bad update).
    /// </summary>
    private static bool IsGameBananaUpdateAvailable(GIMI_ModManager.Core.Services.GameBanana.Models.ModPageInfo modInfo,
        string localModFolder)
    {
        var newestGbDate = modInfo.Files.Select(f => f.DateAdded).OrderByDescending(d => d).FirstOrDefault();
        if (newestGbDate == default)
            return false;

        DateTime newestLocal;
        try
        {
            newestLocal = Directory.EnumerateFiles(localModFolder, "*", SearchOption.AllDirectories)
                .Select(File.GetLastWriteTime)
                .OrderByDescending(d => d)
                .FirstOrDefault();
        }
        catch (Exception e)
        {
            Log.ForContext<ModPaneVM>().Warning(e, "Failed to read local mod file timestamps for update check");
            return false;
        }

        if (newestLocal == default)
            return false;

        return newestGbDate > newestLocal;
    }

    private string BuildSearchTerms()
    {
        var folderName = _loadedMod!.Mod.Name;
        if (string.IsNullOrWhiteSpace(folderName))
            folderName = _loadedMod!.Mod.GetDisplayName();

        // Strip a leading disabled prefix and normalize separators to spaces, drop a trailing
        // version number (e.g. "_v11" / "_2") commonly appended to mod folder names.
        folderName = ModFolderHelpers.GetFolderNameWithoutDisabledPrefix(folderName) ?? folderName;
        folderName = folderName.Replace('_', ' ').Replace('-', ' ').Trim();
        folderName = System.Text.RegularExpressions.Regex.Replace(folderName, @"\s+\d+$", "");

        var terms = new List<string>();
        if (!string.IsNullOrWhiteSpace(folderName)) terms.Add(folderName);

        var character = _loadedMod!.ModList.Character;
        var charName = (!string.IsNullOrWhiteSpace(character.DisplayName)
            ? character.DisplayName
            : character.InternalName.Id)?.Trim();
        if (!string.IsNullOrWhiteSpace(charName)) terms.Add(charName);

        return string.Join(" ", terms);
    }

    private static int? GetGameRowId(Uri gameBananaUrl)
    {
        if (gameBananaUrl is null || !gameBananaUrl.IsAbsoluteUri) return null;
        if (!gameBananaUrl.Host.Equals("gamebanana.com", StringComparison.OrdinalIgnoreCase)) return null;
        var segment = gameBananaUrl.Segments.LastOrDefault()?.TrimEnd('/');
        return int.TryParse(segment, out var id) ? id : null;
    }

    private async Task<(string Url, bool AlwaysRedownload)?> ShowSearchDialogAsync(
        IReadOnlyList<GIMI_ModManager.Core.Services.GameBanana.ApiModels.ApiSearchModResult> results,
        string initialTerms, int? gameRowId)
    {
        var picker = new GameBananaSearchPicker();
        var includeTools = GameBananaService.ShouldIncludeTools(_loadedMod?.ModList.Character);
        picker.Initialize(initialTerms, gameRowId, _loadedMod?.Mod.FullPath,
            (terms, ct) => _gameBananaService.SearchModsAsync(terms, gameRowId, includeTools, ct));
        picker.SetResults(results);

        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Title = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("ModPane_SearchResultsTitle") ?? "Search GameBanana",
            PrimaryButtonText = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("ModPane_UseThisMod") ?? "Use this mod",
            CloseButtonText = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("Common_Cancel") ?? "Cancel",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            Content = picker
        };

        var result = await dialog.ShowAsync();

        if (result != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
            return null;

        if (picker.SelectedResult?.ModId > 0)
        {
            // Use the API-provided profile URL — correct for both mods and tools.
            var url = picker.SelectedResult.ProfileUrl ??
                      GameBananaService.BuildModUrlFromId(picker.SelectedResult.ModId).ToString();
            return (url, picker.AlwaysRedownload);
        }

        return null;
    }

    private bool CanOpenModFolder() => DefaultCanExecute;

    [RelayCommand(CanExecute = nameof(CanOpenModFolder))]
    private async Task OpenModFolderAsync()
    {
        await CommandWrapper(async () =>
        {
            if (!IsModLoaded) return;
            await Windows.System.Launcher.LaunchFolderAsync(
                await StorageFolder.GetFolderFromPathAsync(_loadedMod.Mod.FullPath));
        }).ConfigureAwait(false);
    }

    [RelayCommand]
    private void ToggleEditingModName()
    {
        IsEditingModName = !IsEditingModName;
    }

    #endregion

    #region DragAndDropHandlers

    public bool CanSetImageFromDragDropWeb(Uri? url)
    {
        if (!DefaultCanExecute)
            return false;

        if (url is null || !url.IsAbsoluteUri)
            return false;

        if (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
            return false;

        return Constants.SupportedImageExtensions.Contains(Path.GetExtension(url.AbsolutePath));
    }

    public async Task SetImageFromDragDropWeb(Uri uri)
    {
        await CommandWrapper(async () =>
        {
            var image = await _imageHandlerService.DownloadImageAsync(uri, _cancellationToken);
            ModModel.ImageUri = new Uri(image.Path);
        }, true, useDefaultExceptionHandler: true).ConfigureAwait(false);
    }

    public bool CanSetImageFromDragDropStorageItem(IReadOnlyList<IStorageItem> storageItems)
    {
        if (!DefaultCanExecute)
            return false;

        if (storageItems.Count != 1)
            return false;

        var file = storageItems.First();

        if (!Uri.TryCreate(file.Path, UriKind.Absolute, out _))
            return false;

        return Constants.SupportedImageExtensions.Contains(Path.GetExtension(file.Name));
    }

    public async Task SetImageFromDragDropFile(IReadOnlyList<IStorageItem> storageItems)
    {
        await CommandWrapper(() =>
        {
            var file = storageItems.First();

            var filePath = new Uri(file.Path);

            ModModel.ImageUri = filePath;
            return Task.CompletedTask;
        }, true, useDefaultExceptionHandler: true).ConfigureAwait(false);
    }

    #endregion

    private async Task CommandWrapper(Func<Task> command, bool hardBusy = false, Action<Exception>? uncaughtErrorHandler = null,
        bool useDefaultExceptionHandler = false, [CallerMemberName] string commandName = "")
    {
        try
        {
            using var _ = await LockAsync().ConfigureAwait(false);
            using var busy = hardBusy ? BusySetter.StartHardBusy() : BusySetter.StartSoftBusy();

            await command();
        }
        catch (TaskCanceledException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            if (useDefaultExceptionHandler)
            {
                _logger.Error(e, "An error occured while executing command {CommandName}", commandName);
                _notificationService.ShowNotification(string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("CharDetails_ErrorRunCommand") ?? "An error occured running command {0}", commandName), e.Message, null);
                return;
            }

            if (uncaughtErrorHandler is null) throw;

            var ex = ExceptionDispatchInfo.Capture(e);
            uncaughtErrorHandler(ex.SourceException);
        }
    }

    private async Task CommandWrapper(Action command, bool hardBusy = false)
    {
        try
        {
            using var _ = await LockAsync().ConfigureAwait(false);
            using var busy = hardBusy ? BusySetter.StartHardBusy() : BusySetter.StartSoftBusy();
            command();
        }
        catch (TaskCanceledException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<IDisposable> LockAsync() =>
        await _loadModLock.LockAsync(cancellationToken: _cancellationToken).ConfigureAwait(false);

    private IRelayCommand[]? _viewModelCommands;

    private void NotifyAllCommands()
    {
        if (_viewModelCommands is null)
        {
            var commands = new List<IRelayCommand>();
            foreach (var propertyInfo in GetType()
                         .GetProperties()
                         .Where(p => p.PropertyType.IsAssignableTo(typeof(IRelayCommand))))
            {
                var value = propertyInfo.GetValue(this);

                if (value is IRelayCommand relayCommand)
                    commands.Add(relayCommand);
            }

            _viewModelCommands = commands.ToArray();
        }

        _viewModelCommands.ForEach(c => c.NotifyCanExecuteChanged());
    }
}

public partial class ModPaneFieldsVm : ObservableObject
{
    public bool IsLoaded { get; private init; }
    public ModPaneFieldsVm? UnchangedValue { get; private init; }

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private Uri _imageUri = ImageHandlerService.StaticPlaceholderImageUri;
    public bool IsImageUriChanged => ImageUri != UnchangedValue?.ImageUri;
    [ObservableProperty] private string _modDisplayName = string.Empty;
    public bool IsModDisplayNameChanged => ModDisplayName != UnchangedValue?.ModDisplayName;
    [ObservableProperty] private string _modUrl = string.Empty;
    public bool IsModUrlChanged => ModUrl != UnchangedValue?.ModUrl;
    [ObservableProperty] private string? _modIniPath = null;
    public bool IsModIniPathChanged => ModIniPath != UnchangedValue?.ModIniPath;
    [ObservableProperty] private bool _ignoreMergedIni = true;
    public bool IsIgnoreMergedIniChanged => IgnoreMergedIni != UnchangedValue?.IgnoreMergedIni;

    public ObservableCollection<ModPaneFieldsKeySwapVm> KeySwaps { get; } = new();
    public bool IsKeySwapsChanged => AnyKeySwapChanges();

    public string IsKeySwapManagementEnabled => (!IgnoreMergedIni).ToString().ToLower();

    private ModPaneFieldsVm(CharacterSkinEntry modEntry, ModSettings modSettings, IEnumerable<KeySwapSection> keySwaps)
    {
        IsEnabled = modEntry.IsEnabled;
        ImageUri = modSettings.ImagePath ?? ImageHandlerService.StaticPlaceholderImageUri;
        ModDisplayName = modEntry.Mod.GetDisplayName();
        ModUrl = modSettings.ModUrl?.ToString() ?? "";
        ModIniPath = modSettings.MergedIniPath?.ToString();
        IgnoreMergedIni = modSettings.IgnoreMergedIni;

        foreach (var keySwap in keySwaps)
        {
            KeySwaps.Add(new ModPaneFieldsKeySwapVm()
            {
                ForwardHotkey = keySwap.ForwardKey,
                BackwardHotkey = keySwap.BackwardKey,
                SectionKey = keySwap.SectionName,
                Type = keySwap.Type,
                VariationsCount = keySwap.Variants?.ToString() ?? App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("ModPane_Unknown") ?? "Unknown"
            });

            KeySwaps.Last().PropertyChanged += (_, e) => { OnPropertyChanged(nameof(KeySwaps)); };
        }

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AnyChanges))
                OnPropertyChanged(nameof(AnyChanges));
        };
    }

    public ModPaneFieldsVm()
    {
    }

    public static ModPaneFieldsVm FromModEntry(CharacterSkinEntry modEntry, ModSettings modSettings, ICollection<KeySwapSection> keySwaps)
    {
        return new ModPaneFieldsVm(modEntry, modSettings, keySwaps)
        {
            UnchangedValue = new ModPaneFieldsVm(modEntry, modSettings, keySwaps),
            IsLoaded = true
        };
    }

    public bool AnyChanges
    {
        get
        {
            var anyChanges = false;

            if (UnchangedValue is null)
                return false;

            anyChanges |= IsEnabled != UnchangedValue.IsEnabled;
            anyChanges |= ImageUri != UnchangedValue.ImageUri;
            anyChanges |= ModDisplayName != UnchangedValue.ModDisplayName;
            anyChanges |= ModUrl != UnchangedValue.ModUrl;
            anyChanges |= ModIniPath != UnchangedValue.ModIniPath;
            anyChanges |= IgnoreMergedIni != UnchangedValue.IgnoreMergedIni;

            if (KeySwaps.Count != UnchangedValue.KeySwaps.Count)
                return true;

            if (anyChanges)
                return true;

            anyChanges |= AnyKeySwapChanges();

            return anyChanges;
        }
    }

    private bool AnyKeySwapChanges()
    {
        var anyChanges = false;

        if (UnchangedValue is null)
            return false;

        if (UnchangedValue.IgnoreMergedIni)
            return false;

        if (KeySwaps.Count != UnchangedValue.KeySwaps.Count)
            return true;

        for (var i = 0; i < KeySwaps.Count; i++)
        {
            var oldKeySwap = UnchangedValue.KeySwaps[i];
            var newKeySwap = KeySwaps[i];

            anyChanges |= (oldKeySwap.ForwardHotkey ?? "") != (newKeySwap.ForwardHotkey ?? "");
            anyChanges |= (oldKeySwap.BackwardHotkey ?? "") != (newKeySwap.BackwardHotkey ?? "");
        }

        return anyChanges;
    }
}

[DebuggerDisplay("Section: {_sectionKey} - {_forwardHotkey} - {_backwardHotkey}")]
public partial class ModPaneFieldsKeySwapVm : ObservableObject
{
    [ObservableProperty] private string _sectionKey = string.Empty;

    [ObservableProperty] private string? _condition;
    [ObservableProperty] private string? _forwardHotkey;
    [ObservableProperty] private string? _backwardHotkey;
    [ObservableProperty] private string? _type;
    [ObservableProperty] private string _variationsCount = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("ModPane_Unknown") ?? "Unknown";
}