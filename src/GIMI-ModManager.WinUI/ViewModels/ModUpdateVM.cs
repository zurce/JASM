using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GIMI_ModManager.Core.Contracts.Entities;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Entities.Mods.Exceptions;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.Core.Services;
using GIMI_ModManager.Core.Services.GameBanana;
using GIMI_ModManager.Core.Services.GameBanana.Models;
using GIMI_ModManager.Core.Entities.Mods.FileModels;
using GIMI_ModManager.WinUI.Services.ModHandling;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.ViewModels.ModPageViewModels;
using Microsoft.UI.Dispatching;
using Serilog;

namespace GIMI_ModManager.WinUI.ViewModels;

public partial class ModUpdateVM : ObservableRecipient
{
    private readonly GameBananaCoreService _gameBananaCoreService = App.GetService<GameBananaCoreService>();
    private readonly ISkinManagerService _skinManagerService = App.GetService<ISkinManagerService>();
    private readonly ModNotificationManager _modNotificationManager = App.GetService<ModNotificationManager>();
    private readonly NotificationManager _notificationManager = App.GetService<NotificationManager>();
    private readonly ModInstallerService _modInstallerService = App.GetService<ModInstallerService>();
    private readonly ArchiveService _archiveService = App.GetService<ArchiveService>();

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Guid _notificationId;
    private ICharacterModList _characterModList = null!;
    private readonly WindowEx _window;
    private ModNotification? _notification;
    private List<ModFileInfo> _modFiles = new();
    private readonly CancellationToken _ct;

    private ModPageInfo? _modPageInfo;
    private bool _isTool;
    private ISkinMod? _existingModToUpdate;

    [ObservableProperty] private string _initializing = "true";

    /// <summary>True when the submission has more than one file, i.e. variants are possible.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVariantsAvailable), nameof(InstallAsVariantsVisibility))]
    private bool _hasMultipleFiles = false;

    /// <summary>Variant-selection mode: checkboxes shown per file, batch download enabled.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVariantsAvailable), nameof(InstallAsVariantsVisibility), nameof(VariantModeVisibility))]
    [NotifyCanExecuteChangedFor(nameof(DownloadVariantsCommand))]
    private bool _isVariantMode = false;

    public bool IsVariantsAvailable => HasMultipleFiles && !IsVariantMode;

    // Root-level x:Bind on a WindowEx can't use value converters — expose ready-made Visibility values.
    public Microsoft.UI.Xaml.Visibility InstallAsVariantsVisibility =>
        IsVariantsAvailable ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    // In variant-selection mode the bottom-right button becomes "Download".
    public Microsoft.UI.Xaml.Visibility VariantModeVisibility =>
        IsVariantMode ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    [ObservableProperty] private string _modName = string.Empty;

    [ObservableProperty] private Uri _modPage = new("https://gamebanana.com/");

    [ObservableProperty] private Uri? _modPath = null;

    [ObservableProperty] private DateTime _lastUpdateCheck = DateTime.Now;

    [ObservableProperty] private bool _isOpenDownloadButtonEnabled = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(IgnoreAndCloseCommand), nameof(StartDownloadCommand),
        nameof(StartInstallCommand), nameof(ToggleVariantModeCommand), nameof(DownloadVariantsCommand))]
    private bool _isWindowBusy = false;

    public bool IsNotBusy => !IsWindowBusy;


    public readonly ObservableCollection<ModFileInfoVm> ModFileInfos = new();
    private readonly ILogger _logger = App.GetService<ILogger>().ForContext<ModUpdateVM>();

    public ModUpdateVM(Guid notificationId, WindowEx window, CancellationToken ctsToken)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _notificationId = notificationId;
        _window = window;
        _ct = ctsToken;
        Initialize();
    }

    private async void Initialize()
    {
        try
        {
            await InternalInitialize();
            Initializing = "false";
        }
        catch (OperationCanceledException e)
        {
            _window.Close();
        }
        catch (Exception e)
        {
            await LogErrorAndClose(e);
        }
    }


    private async Task InternalInitialize()
    {
        IsWindowBusy = true;
        _notification =
            await _modNotificationManager.GetNotificationById(_notificationId);

        if (_notification?.ModsRetrievedResult is null)
        {
            await LogErrorAndClose(new InvalidOperationException("Failed to get mod page info, mod info is missing"));
            return;
        }

        var characterSkinEntry = _skinManagerService.GetModEntryById(_notification.ModId);

        if (characterSkinEntry is null)
        {
            await LogErrorAndClose(new InvalidOperationException($"Mod with id {_notification.ModId} not found"));
            return;
        }

        _existingModToUpdate = characterSkinEntry.Mod;

        _characterModList = characterSkinEntry.ModList;
        var mod = characterSkinEntry.Mod;

        var modSettings = mod.Settings.GetSettingsLegacy().TryPickT0(out var settings, out _) ? settings : null;

        if (modSettings is null)
        {
            await LogErrorAndClose(new ModSettingsNotFoundException($"Mod settings not found for mod {mod.FullPath}"));
            return;
        }

        if (!await _gameBananaCoreService.HealthCheckAsync(_ct))
        {
            await LogErrorAndClose(
                new InvalidOperationException("Failed to get mod page info, GameBanana Api is not available"),
                removeNotification: false);
            return;
        }

        using (var _ = IgnorePollyLimiterScope.Ignore())
        {
            // Tools and mods share the same numeric id space — route by the stored profile URL so the
            // correct submission's profile and file list are fetched.
            var sitePageUrl = _notification.ModsRetrievedResult.SitePageUrl;
            _isTool = sitePageUrl?.Segments.Any(s => s.Equals("tools/", StringComparison.OrdinalIgnoreCase)) ?? false;
            _modPageInfo = _isTool
                ? await _gameBananaCoreService.GetToolProfileAsync(new GbModId(_notification.ModsRetrievedResult.ModId), _ct)
                : await _gameBananaCoreService.GetModProfileAsync(new GbModId(_notification.ModsRetrievedResult.ModId), _ct);
        }

        if (_modPageInfo is null)
        {
            await LogErrorAndClose(new InvalidOperationException("Failed to get mod page info, mod does not exist"), removeNotification: false);
            return;
        }

        ModName = modSettings.CustomName ?? mod.Name;
        if (_modPageInfo.ModPageUrl is not null)
            ModPage = _modPageInfo.ModPageUrl;

        ModPath = new Uri(mod.FullPath);
        LastUpdateCheck = _notification.ModsRetrievedResult.LastCheck;


        _modFiles = _modPageInfo.Files.ToList();
        HasMultipleFiles = _modFiles.Count > 1;

        foreach (var modFile in _modFiles)
        {
            var vm = new ModFileInfoVm(modFile, StartDownloadCommand, StartInstallCommand)
            {
                IsNew = modFile.DateAdded > LastUpdateCheck,
                IsBusy = true
            };
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ModFileInfoVm.IsVariantSelected))
                {
                    DownloadVariantsCommand.NotifyCanExecuteChanged();
                    SyncAddonCheckboxes(vm);
                }
            };
            await InitializeModFileVmAsync(vm);
        }

        IsWindowBusy = false;
    }

    private async Task LogErrorAndClose(Exception e, bool removeNotification = true)
    {
        _logger.Error(e, "Failed to get mod update info");
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            App.GetService<NotificationManager>().ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("ModUpdate_FailedGetInfo") ?? "Failed to get mod update info",
                e.Message, TimeSpan.FromSeconds(10));
        });
        _window.Close();

        if (!removeNotification)
            return;

        var notification = await _modNotificationManager.GetNotificationById(_notificationId);
        if (notification is null)
        {
            return;
        }

        await _modNotificationManager.RemoveModNotificationAsync(notification.Id);
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task IgnoreAndCloseAsync()
    {
        var notification = await _modNotificationManager.GetNotificationById(_notificationId);
        if (notification is null)
        {
            return;
        }

        await _modNotificationManager.RemoveModNotificationAsync(notification.Id);
        _window.Close();
    }


    private async Task InitializeModFileVmAsync(ModFileInfoVm fileInfoVm)
    {
        ArgumentNullException.ThrowIfNull(fileInfoVm);

        var existingArchive = await _gameBananaCoreService.GetLocalModArchiveByMd5HashAsync(fileInfoVm.Md5Hash, _ct);

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (existingArchive is not null)
            {
                fileInfoVm.Status = ModFileInfoVm.InstallStatus.Downloaded;
                fileInfoVm.DownloadProgress = 100;
                fileInfoVm.ArchiveFile = new FileInfo(existingArchive.FullName);
            }

            fileInfoVm.IsBusy = false;
        });
    }

    private bool CanStartDownload(ModFileInfoVm? fileInfoVm)
    {
        if (fileInfoVm is null)
            return false;

        var anyOtherDownloading = ModFileInfos.Any(x =>
            fileInfoVm.FileId != x.FileId && x.Status == ModFileInfoVm.InstallStatus.Downloading);

        var canDownload = IsNotBusy && !fileInfoVm.IsBusy && fileInfoVm.Status == ModFileInfoVm.InstallStatus.NotStarted
                          && !anyOtherDownloading && fileInfoVm.ArchiveFile is null &&
                          !fileInfoVm.ModId.IsNullOrEmpty() && !fileInfoVm.FileId.IsNullOrEmpty();


        return canDownload;
    }

    [RelayCommand(CanExecute = nameof(CanStartDownload))]
    private async Task StartDownload(ModFileInfoVm fileInfoVm)
    {
        try
        {
            fileInfoVm.Status = ModFileInfoVm.InstallStatus.Downloading;
            fileInfoVm.IsBusy = true;

            var identifier = new GbModFileIdentifier(new GbModId(fileInfoVm.ModId), new GbModFileId(fileInfoVm.FileId),
                IsTool: _isTool);

            var archivePath =
                await Task.Run(() => _gameBananaCoreService.DownloadModAsync(identifier, fileInfoVm.Progress, _ct),
                    _ct);

            fileInfoVm.ArchiveFile = new FileInfo(archivePath);
            fileInfoVm.Status = ModFileInfoVm.InstallStatus.Downloaded;
        }
        catch (Exception) when (_ct.IsCancellationRequested)
        {
            Reset();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to download mod file");

            _notificationManager.ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("ModUpdate_FailedDownload") ?? "Failed to download mod file",
                e.Message, TimeSpan.FromSeconds(10));

            Reset();
        }
        finally
        {
            fileInfoVm.IsBusy = false;
        }

        void Reset()
        {
            fileInfoVm.Status = ModFileInfoVm.InstallStatus.NotStarted;
            fileInfoVm.ArchiveFile = null;
            fileInfoVm.DownloadProgress = 0;
        }
    }

    private bool CanInstall(ModFileInfoVm? fileInfoVm)
    {
        if (fileInfoVm is null)
            return false;

        var anyOtherInstalling = ModFileInfos.Any(x =>
            fileInfoVm.FileId != x.FileId && x.Status == ModFileInfoVm.InstallStatus.Installing);

        var canInstall = IsNotBusy && !fileInfoVm.IsBusy &&
                         fileInfoVm.Status == ModFileInfoVm.InstallStatus.Downloaded &&
                         !anyOtherInstalling && fileInfoVm.ArchiveFile is not null;

        return canInstall;
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private void ToggleVariantMode() => SetVariantMode(!IsVariantMode, clearNesting: true);

    private void ExitVariantMode() => SetVariantMode(false, clearNesting: false);

    private void SetVariantMode(bool enabled, bool clearNesting)
    {
        IsVariantMode = enabled;

        foreach (var fileInfoVm in ModFileInfos)
        {
            fileInfoVm.ShowVariantCheckbox = enabled;
            if (!enabled)
            {
                fileInfoVm.IsVariantSelected = false;
                // Cancelling multi-install resets nesting; the install path preserves it.
                if (clearNesting)
                {
                    fileInfoVm.AddonParent = null;
                    fileInfoVm.IsDropTarget = false;
                }
            }
        }
    }

    /// <summary>
    /// Nests a file under another as an add-on (multi-install drag). Null target detaches.
    /// Depth-1 only: targets must be top-level and dragged files must not own add-ons.
    /// Dragging implies include, so the nested file is checked.
    /// </summary>
    public bool AttachAsAddon(ModFileInfoVm dragged, ModFileInfoVm? target)
    {
        if (!IsVariantMode || dragged is null)
            return false;
        if (target is null)
        {
            dragged.AddonParent = null;
            return true;
        }
        if (ReferenceEquals(dragged, target) || target.AddonParent is not null)
            return false;
        if (ModFileInfos.Any(x => ReferenceEquals(x.AddonParent, dragged)))
            return false;
        dragged.AddonParent = target;
        dragged.IsVariantSelected = true;
        // Explicit (not via notification): re-setting an already-checked file fires nothing.
        if (!target.IsVariantSelected)
            target.IsVariantSelected = true;
        // Keep the parent directly above its children in the list.
        ModFileInfos.Remove(dragged);
        ModFileInfos.Insert(ModFileInfos.IndexOf(target) + 1, dragged);
        return true;
    }

    /// <summary>
    /// Keeps attach checkboxes consistent: checking a nested file checks its parent;
    /// unchecking a parent unchecks its nested files. Terminates (depth-1, guarded).
    /// </summary>
    private void SyncAddonCheckboxes(ModFileInfoVm changed)
    {
        if (changed.IsVariantSelected && changed.AddonParent is not null)
            changed.AddonParent.IsVariantSelected = true;
        else if (!changed.IsVariantSelected)
        {
            foreach (var child in ModFileInfos.Where(x => ReferenceEquals(x.AddonParent, changed)))
                child.IsVariantSelected = false;
        }
    }

    private bool CanDownloadVariants()
    {
        if (!IsVariantMode || !IsNotBusy)
            return false;

        var selected = ModFileInfos.Where(x => x.IsVariantSelected).ToList();
        if (selected.Count == 0)
            return false;

        // No file may be mid-download/install while starting a variant batch.
        var anyBusy = ModFileInfos.Any(x => x.Status is ModFileInfoVm.InstallStatus.Downloading
            or ModFileInfoVm.InstallStatus.Installing or ModFileInfoVm.InstallStatus.Installed);

        return !anyBusy && selected.Any(x => x.Status == ModFileInfoVm.InstallStatus.NotStarted ||
                                            x.Status == ModFileInfoVm.InstallStatus.Downloaded);
    }

    /// <summary>
    /// Batch-download all selected files (sequentially), then install them as variants of the
    /// updated mod. Single selection falls back to the normal single-file update flow.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDownloadVariants))]
    private async Task DownloadVariantsAsync()
    {
        var selected = ModFileInfos.Where(x => x.IsVariantSelected).ToList();
        if (selected.Count == 0)
            return;

        IsWindowBusy = true;
        try
        {
            foreach (var fileInfoVm in selected.Where(x => x.Status == ModFileInfoVm.InstallStatus.NotStarted))
            {
                await StartDownload(fileInfoVm);
                if (fileInfoVm.Status != ModFileInfoVm.InstallStatus.Downloaded)
                {
                    // Download failed/cancelled — stop the batch here.
                    return;
                }
            }

            var downloaded = selected.Where(x => x.ArchiveFile is not null &&
                                                 x.Status == ModFileInfoVm.InstallStatus.Downloaded).ToList();
            if (downloaded.Count == 0)
                return;

            ExitVariantMode();

            if (downloaded.Count > 1)
                await InstallVariantsAsync(downloaded, downloaded[0]);
            else
                await StartInstall(downloaded[0]);
        }
        finally
        {
            IsWindowBusy = false;
        }
    }

    /// <summary>
    /// Installs several downloaded archives as one variant-aware update of the existing mod:
    /// &lt;top&gt;/{ .JASM_ModConfig.json, ramielle_mod/, DISABLED_pink/, ... }.
    /// The whole structure goes through the normal installer with the existing mod marked for overwrite.
    /// </summary>
    private async Task InstallVariantsAsync(IReadOnlyList<ModFileInfoVm> files, ModFileInfoVm mainFile)
    {
        IsWindowBusy = true;
        try
        {
            foreach (var f in files)
                f.Status = ModFileInfoVm.InstallStatus.Installing;

            var result = await Task.Run(async () =>
            {
                var tmpRoot = App.GetUniqueTmpFolder();

                var mainSections = Path.GetFileName(mainFile.ArchiveFile!.FullName)
                    .Split(ModArchiveRepository.Separator);
                if (mainSections.Length != 4)
                    throw new InvalidArchiveNameFormatException();

                // Top-level folder keeps the original archive base name, e.g. "coolmod"
                var topFolder = tmpRoot.CreateSubdirectory(mainSections[0]);

                // Partition checked files: top-level files become exclusive variants,
                // dragged-nested files become add-ons. All live flat side by side under
                // the top folder — add-ons belong to the main mod, not to any variant.
                var roots = files.Where(f => f.AddonParent is null).ToList();
                if (roots.Count == 0)
                    roots = [.. files]; // degenerate: no top-level files, all act as roots
                var mainRoot = roots.FirstOrDefault(r => ReferenceEquals(r, mainFile)) ?? roots[0];

                var baseNames = new Dictionary<ModFileInfoVm, string>();
                foreach (var file in files)
                {
                    var modFolder = _archiveService.ExtractArchive(file.ArchiveFile!.FullName,
                        App.GetUniqueTmpFolder().FullName);

                    var sections = Path.GetFileName(file.ArchiveFile.FullName).Split(ModArchiveRepository.Separator);
                    if (sections.Length != 4)
                        throw new InvalidArchiveNameFormatException();
                    baseNames[file] = sections[0];

                    // Roots use exclusive variant naming (main enabled, rest disabled);
                    // nested files extract flat first and are relocated into their parent below.
                    var folderName = roots.Contains(file)
                        ? (ReferenceEquals(file, mainRoot)
                            ? sections[0]
                            : VariantFolderHelpers.GetDisabledVariantFolderName(sections[0]))
                        : sections[0];

                    var dest = Path.Combine(topFolder.FullName, folderName);
                    modFolder.MoveTo(dest);
                }

                // Add-ons belong to the main mod: nested files install flat, all enabled.
                var placedAddonNames = files
                    .Where(f => f.AddonParent is not null && !roots.Contains(f))
                    .Select(f => baseNames[f]).ToList();

                // Write the initial .JASM_ModConfig.json with both arrays
                // A single root is just the mod itself — variants only exist when there is
                // an actual exclusive choice between two or more top-level files.
                var variantNames = roots.Select(f => baseNames[f]).ToList();
                var variants = roots.Count > 1
                    ? VariantManager.CreateInitialVariants(variantNames, baseNames[mainRoot])
                    : null;
                var addons = AddonManager.CreateInitialAddons(placedAddonNames);

                var settings = new JsonModSettings
                {
                    Id = Guid.NewGuid().ToString(),
                    DateAdded = DateTime.Now.ToString(CultureInfo.CurrentCulture),
                    Variants = variants?.Select(v => new JsonVariantEntry
                    {
                        Name = v.Name,
                        FolderName = v.FolderName,
                        Enabled = v.Enabled
                    }).ToList(),
                    Addons = addons.Count == 0 ? null : addons.Select(a => new JsonAddonEntry
                    {
                        Name = a.Name,
                        FolderName = a.FolderName,
                        Enabled = a.Enabled
                    }).ToList()
                };

                await File.WriteAllTextAsync(
                    Path.Combine(topFolder.FullName, Constants.ModConfigFileName),
                    JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }), _ct);

                var modUrl = _modPageInfo?.ModPageUrl;

                using var task = await _modInstallerService.StartModInstallationAsync(topFolder, _characterModList,
                    setup: options =>
                    {
                        options.ModUrl = modUrl;
                        options.ExistingModToOverwritePath = _existingModToUpdate?.FullPath;
                    }).ConfigureAwait(false);

                return await task.WaitForCloseAsync(_ct).ConfigureAwait(false);
            }, _ct);

            if (result.CloseReason == CloseRequestedArgs.CloseReasons.Error)
                throw new Exception("An error occured during mod install, see logs", result.Exception);

            if (result.CloseReason == CloseRequestedArgs.CloseReasons.Canceled)
            {
                foreach (var f in files)
                    f.Status = ModFileInfoVm.InstallStatus.Downloaded;
                return;
            }

            foreach (var f in files)
                f.Status = ModFileInfoVm.InstallStatus.Installed;
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to install mod variants");

            _notificationManager.ShowNotification("Failed to install mod variants",
                e.InnerException?.Message ?? e.Message, TimeSpan.FromSeconds(10));

            foreach (var f in files)
                f.Status = ModFileInfoVm.InstallStatus.Downloaded;
        }
        finally
        {
            IsWindowBusy = false;
        }
    }


    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task StartInstall(ModFileInfoVm fileInfoVm)
    {
        // In variant-selection mode, installing any marked file installs ALL marked files as variants.
        if (IsVariantMode)
        {
            var selected = ModFileInfos.Where(x => x.IsVariantSelected && x.ArchiveFile is not null &&
                                                   x.Status == ModFileInfoVm.InstallStatus.Downloaded).ToList();
            if (selected.Count > 1)
            {
                var main = selected.FirstOrDefault(x => ReferenceEquals(x, fileInfoVm)) ?? selected[0];
                ExitVariantMode();
                await InstallVariantsAsync(selected, main);
                return;
            }
        }

        IsWindowBusy = true;
        fileInfoVm.IsBusy = true;

        fileInfoVm.Status = ModFileInfoVm.InstallStatus.Installing;

        try
        {
            var result = await Task.Run(async () =>
            {
                var modFolder = _archiveService.ExtractArchive(fileInfoVm.ArchiveFile!.FullName,
                    App.GetUniqueTmpFolder().FullName);

                var archiveNameSections = Path.GetFileName(modFolder.Name).Split(ModArchiveRepository.Separator);
                if (archiveNameSections.Length != 4)
                    throw new InvalidArchiveNameFormatException();

                var modFolderName = archiveNameSections[0];
                var modFolderExt = Path.GetExtension(modFolder.Name);

                var modFolderParent = modFolder.Parent!;

                var zipRoot = Directory.CreateDirectory(Path.Combine(modFolderParent.FullName, "ArchiveRoot"));

                modFolder.MoveTo(Path.Combine(zipRoot.FullName, $"{modFolderName}{modFolderExt}"));

                var modUrl = _modPageInfo?.ModPageUrl;

                using var task = await _modInstallerService.StartModInstallationAsync(zipRoot, _characterModList,
                    setup: options =>
                    {
                        options.ModUrl = modUrl;
                        options.ExistingModToOverwritePath = _existingModToUpdate?.FullPath;
                    }).ConfigureAwait(false);

                return await task.WaitForCloseAsync(_ct).ConfigureAwait(false);
            }, _ct);

            if (result.CloseReason == CloseRequestedArgs.CloseReasons.Error)
            {
                if (result.Exception is not null)
                {
                    throw new Exception("An error occured during mod install, see logs and inner exception",
                        result.Exception);
                }

                throw new Exception("An error occured during mod install, see logs");
            }

            if (result.CloseReason == CloseRequestedArgs.CloseReasons.Canceled)
            {
                fileInfoVm.Status = ModFileInfoVm.InstallStatus.Downloaded;
                return;
            }

            fileInfoVm.Status = ModFileInfoVm.InstallStatus.Installed;
        }
        catch (TaskCanceledException)
        {
            fileInfoVm.Status = ModFileInfoVm.InstallStatus.Downloaded;
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to install mod file");

            _notificationManager.ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("ModUpdate_FailedInstall") ?? "Failed to install mod file",
                e.InnerException?.Message ?? e.Message, TimeSpan.FromSeconds(10));

            fileInfoVm.Status = ModFileInfoVm.InstallStatus.Downloaded;
        }
        finally
        {
            IsWindowBusy = false;
            fileInfoVm.IsBusy = false;
        }
    }
}