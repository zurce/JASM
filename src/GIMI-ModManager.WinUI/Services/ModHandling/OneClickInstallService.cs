using System.IO;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.GamesService.Interfaces;
using GIMI_ModManager.Core.GamesService.Models;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.Core.Services;
using GIMI_ModManager.Core.Services.GameBanana;
using GIMI_ModManager.Core.Services.GameBanana.Models;
using GIMI_ModManager.Core.Services.Protocol;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GIMI_ModManager.WinUI.Services.AppManagement;
using GIMI_ModManager.WinUI.Services.ModHandling;
using GIMI_ModManager.WinUI.Services.Notifications;
using Serilog;

namespace GIMI_ModManager.WinUI.Services.AppManagement;

/// <summary>
/// Runs a GameBanana 1-click link: resolve the submission's game and character, download the linked file and
/// hand it to the existing Mod Installer.
///
/// Deliberately reuses the normal install path (<see cref="ModInstallerService"/>) instead of a second install
/// implementation, so variants, add-ons, "Install in Skin" and the XXMI/direct 3DMigoto handling behave exactly
/// as for a manual install. The installer window is the confirmation step; the pre-install dialog and the
/// "switch game?" prompt are follow-ups (see the task doc).
/// </summary>
public sealed class OneClickInstallService
{
    private readonly GameBananaCoreService _gameBananaCoreService;
    private readonly IGameService _gameService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly ModInstallerService _modInstallerService;
    private readonly ArchiveService _archiveService;
    private readonly SelectedGameService _selectedGameService;
    private readonly ImageHandlerService _imageHandlerService;
    private readonly OneClickLaunchService _oneClickLaunchService;
    private readonly NotificationManager _notificationManager;
    private readonly ILanguageLocalizer _localizer;
    private readonly ILogger _logger;

    public OneClickInstallService(GameBananaCoreService gameBananaCoreService,
        IGameService gameService,
        ISkinManagerService skinManagerService,
        ModInstallerService modInstallerService,
        ArchiveService archiveService,
        SelectedGameService selectedGameService,
        ImageHandlerService imageHandlerService,
        OneClickLaunchService oneClickLaunchService,
        NotificationManager notificationManager,
        ILanguageLocalizer localizer,
        ILogger logger)
    {
        _gameBananaCoreService = gameBananaCoreService;
        _gameService = gameService;
        _skinManagerService = skinManagerService;
        _modInstallerService = modInstallerService;
        _archiveService = archiveService;
        _selectedGameService = selectedGameService;
        _imageHandlerService = imageHandlerService;
        _oneClickLaunchService = oneClickLaunchService;
        _notificationManager = notificationManager;
        _localizer = localizer;
        _logger = logger.ForContext<OneClickInstallService>();
    }

    /// <summary>Runs the link. Safe to call from the UI thread; the installer is opened on it.</summary>
    public async Task HandleAsync(OneClickInstallRequest request, CancellationToken ct = default)
    {
        try
        {
            _logger.Information("Handling 1-click link for mod {ModId} / file {FileId}", request.ModId,
                request.ModFileId);

            // Resolution is network-bound and can queue behind the background update checker, so it gets the
            // same visible progress treatment as the download — otherwise the app just looks frozen.
            var (profile, game, character) = await BusyDialog.RunAsync(
                Format("OneClick_Checking_Status", "Checking GameBanana…"),
                async (setStatus, token) =>
                {
                    var modProfile = await _gameBananaCoreService
                        .GetModProfileAsync(new GbModId(request.ModId), token)
                        .ConfigureAwait(true);

                    _logger.Information("1-click link mod {ModId}: profile {Profile}", request.ModId,
                        modProfile is null
                            ? "not found"
                            : $"'{modProfile.ModName}' game={modProfile.GameBananaGameName} category={modProfile.GameBananaCategoryName}");

                    if (modProfile is null)
                        return (null, (SupportedGames?)null, (ICharacter?)null);

                    var resolvedGame = await ResolveGameAsync(modProfile.GameBananaGameId).ConfigureAwait(true);
                    if (resolvedGame is null)
                        return (modProfile, (SupportedGames?)null, (ICharacter?)null);

                    var resolvedCharacter = ResolveCharacter(modProfile.GameBananaCategoryName);
                    return (modProfile, resolvedGame, resolvedCharacter);
                },
                ct).ConfigureAwait(true);

            if (profile is null)
            {
                Notify("OneClick_Failed_Title",
                    Format("OneClick_ModNotFound_Message", "GameBanana did not return any info for mod {0}.",
                        request.ModId));
                return;
            }

            if (game is null)
            {
                Notify("OneClick_UnsupportedGame_Title",
                    Format("OneClick_UnsupportedGame_Message", "This mod is for {0}, which JASM+ does not support.",
                        profile.GameBananaGameName is { Length: > 0 } name ? name : "a game"));
                return;
            }

            var selectedGame = await _selectedGameService.GetSelectedGameAsync().ConfigureAwait(true);
            if (!string.Equals(game.Value.ToString(), selectedGame, StringComparison.OrdinalIgnoreCase))
            {
                // Follow-up: a "switch to <game> and install?" prompt. For now, be explicit rather than
                // silently switching the user's whole UI context.
                Notify("OneClick_WrongGame_Title",
                    Format("OneClick_WrongGame_Message", "This mod is for {0}. Switch JASM+ to {0} and click the link again.",
                        game.Value));
                return;
            }

            var modUrl = profile.ModPageUrl;
            var modTitle = profile.ModName ?? character?.DisplayName ?? "?";
            var fileInfo = profile.Files?.FirstOrDefault(file => file.FileId == request.ModFileId.ToString());

            var settings = await _oneClickLaunchService.GetSettingsAsync().ConfigureAwait(true);

            // The alert always asks — a link is triggered from a web page. "Install without asking for
            // confirmation" only removes the *second* confirmation in the Mod Installer Helper, which would
            // otherwise ask the same thing again; the skin picker below exists because of that (when the helper
            // opens it already has one, so offering it twice would be redundant).
            var helperWillOpen = !settings.InstallWithoutConfirmation;

            // The skin picker exists only for the skipped-helper mode: with the helper open it has its own.
            var offerSkinPicker = !helperWillOpen;

            _logger.Information(
                "1-click install for mod {ModId}: asking for a target (character {Character}, Mod Installer Helper {Helper}, skin picker {SkinPicker})",
                request.ModId, character?.DisplayName ?? "not detected", helperWillOpen ? "opens" : "skipped",
                offerSkinPicker ? "shown" : "hidden");

            var chosen = await ConfirmInstallAsync(profile, fileInfo, game.Value, character, modUrl, offerSkinPicker)
                .ConfigureAwait(true);

            if (chosen is null)
            {
                _logger.Information("1-click install for mod {ModId} was cancelled in the install dialog",
                    request.ModId);
                return;
            }

            var target = chosen.Target;
            var targetSkin = chosen.Skin;

            _logger.Information("1-click install for mod {ModId}: target {Target}{Skin}, Mod Installer Helper {Helper}",
                request.ModId, target.DisplayName, targetSkin is null ? string.Empty : $" ({targetSkin.DisplayName})",
                helperWillOpen ? "opens" : "skipped");

            var modList = _skinManagerService.GetCharacterModList(target);

            // A heavy mod takes minutes; without this the app looked frozen (the download used to run with no
            // progress reporting at all). Reuses the same busy dialog as the orphan-mod batch repair.
            var zipRoot = await BusyDialog.RunAsync(
                Format("OneClick_Preparing_Status", "Preparing {0}…", modTitle),
                async (setStatus, token) =>
                {
                    var reported = false;
                    var lastPercent = -1;
                    var lastDecile = -1;
                    var progress = new Progress<int>(percent =>
                    {
                        reported = true;

                        // One log line per 10% keeps this useful for support without flooding the log.
                        if (percent / 10 != lastDecile)
                        {
                            lastDecile = percent / 10;
                            _logger.Information("1-click download progress for mod {ModId}: {Percent}%",
                                request.ModId, percent);
                        }

                        if (percent == lastPercent)
                            return;

                        lastPercent = percent;
                        setStatus(Format("OneClick_Downloading_Status", "Downloading {0} — {1}%", modTitle, percent));
                    });

                    var archivePath = await _gameBananaCoreService
                        .DownloadModAsync(request.Identifier, progress, token)
                        .ConfigureAwait(true);

                    // DownloadModAsync returns an existing local archive without reporting anything.
                    if (!reported)
                        setStatus(Format("OneClick_UsingCachedArchive_Status", "Using the cached archive for {0}…",
                            modTitle));

                    setStatus(Format("OneClick_Extracting_Status", "Extracting {0}…", modTitle));
                    return await Task.Run(() => ExtractToArchiveRoot(archivePath), token).ConfigureAwait(true);
                },
                ct).ConfigureAwait(true);

            // Assets must never require running code, and this archive came off the web: warn (even when
            // confirmation was turned off) when it ships something executable.
            var executableLikeFiles = ArchiveContentScan.FindExecutableLikeFiles(zipRoot.FullName);
            if (executableLikeFiles.Count > 0 &&
                !await ConfirmArchiveContentsAsync(modTitle, executableLikeFiles).ConfigureAwait(true))
            {
                _logger.Information("1-click install for mod {ModId} was cancelled at the content warning",
                    request.ModId);
                TryDeleteTempZipRoot(zipRoot);
                return;
            }

            _logger.Information("Opening the Mod Installer for {ModName} (mod {ModId}, file {FileId})",
                modTitle, request.ModId, request.ModFileId);

            var installOptions = new InstallOptions
            {
                ModUrl = modUrl,
                PreferredSkinInternalName = targetSkin?.InternalName.Id
            };

            if (helperWillOpen)
            {
                await _modInstallerService
                    .StartModInstallationAsync(zipRoot, modList, inGameSkin: null, setup: options =>
                    {
                        options.ModUrl = installOptions.ModUrl;
                        options.PreferredSkinInternalName = installOptions.PreferredSkinInternalName;
                    })
                    .ConfigureAwait(true);
            }
            else
            {
                // The helper fetches the mod's metadata from its URL; skipping it must not leave the mod bare.
                var metadata = new AddModOptions
                {
                    ModName = profile.ModName,
                    Author = profile.AuthorName,
                    Description = profile.Description,
                    ModUrl = modUrl?.ToString()
                };

                var previewImageUrl = profile.PreviewImages?.FirstOrDefault();
                if (previewImageUrl is not null)
                {
                    try
                    {
                        var image = await _imageHandlerService.DownloadImageAsync(previewImageUrl, ct)
                            .ConfigureAwait(true);
                        metadata.ModImage = new Uri(image.Path);
                    }
                    catch (Exception e)
                    {
                        _logger.Warning(e, "Could not download the cover image for mod {ModId}", request.ModId);
                    }
                }

                await _modInstallerService.InstallSilentlyAsync(zipRoot, modList, installOptions, metadata, ct)
                    .ConfigureAwait(true);
            }
        }
        catch (Exception e)
        {
            _logger.Error(e, "1-click install failed for mod {ModId} / file {FileId}", request.ModId, request.ModFileId);
            Notify("OneClick_Failed_Title", Format("OneClick_Failed_Message", "1-click install failed: {0}", e.Message));
        }
    }

    /// <summary>Maps a GameBanana game row id to one of JASM's games via their <c>game.json</c> URLs.</summary>
    private async Task<SupportedGames?> ResolveGameAsync(int? gameBananaGameRowId)
    {
        if (gameBananaGameRowId is null or <= 0)
            return null;

        foreach (var game in Enum.GetValues<SupportedGames>())
        {
            GameInfo? gameInfo;
            try
            {
                gameInfo = await GameService.GetGameInfoAsync(game).ConfigureAwait(true);
            }
            catch (Exception e)
            {
                _logger.Debug(e, "Could not read game info for {Game}", game);
                continue;
            }

            if (gameInfo is null)
                continue;

            if (TryGetGameBananaGameRowId(gameInfo.GameBananaUrl, out var rowId) && rowId == gameBananaGameRowId)
                return game;
        }

        return null;
    }

    /// <summary>Extracts the numeric row id out of a GameBanana game/profile URL.</summary>
    internal static bool TryGetGameBananaGameRowId(Uri? url, out int rowId)
    {
        rowId = 0;
        if (url is null || !url.IsAbsoluteUri)
            return false;

        if (!url.Host.Equals("gamebanana.com", StringComparison.OrdinalIgnoreCase) &&
            !url.Host.Equals("www.gamebanana.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var segment = url.Segments.LastOrDefault()?.TrimEnd('/');
        return int.TryParse(segment, out rowId);
    }

    /// <summary>
    /// Matches GameBanana's category name (for these games: the character, or a section such as "Others")
    /// against the game's characters. Matching is name-based because JASM's own search never used GameBanana
    /// categories — the character was always implied by the page the user was on.
    /// </summary>
    internal ICharacter? ResolveCharacter(string? categoryName) =>
        OneClickCharacterResolver.Resolve(categoryName, _gameService.GetCharacters(includeDisabled: true));

    /// <summary>
    /// Mirrors what the GameBanana download window does before installing: the archive is extracted to a temp
    /// folder and then moved into an "ArchiveRoot" folder, because the Mod Installer treats the single child of
    /// its input folder as the mod root.
    /// </summary>
    private DirectoryInfo ExtractToArchiveRoot(string archivePath)
    {
        var extracted = _archiveService.ExtractArchive(archivePath, App.GetUniqueTmpFolder().FullName);

        // The downloaded archive name is the repository's 4-part contract: <name>_!!_<modId>_!!_<fileId>_!!_<md5><ext>
        var sections = Path.GetFileName(extracted.Name).Split(ModArchiveRepository.Separator);
        if (sections.Length != 4)
            throw new InvalidArchiveNameFormatException();

        var parent = extracted.Parent ?? throw new InvalidOperationException("Extracted archive has no parent folder");
        var zipRoot = parent.CreateSubdirectory("ArchiveRoot");
        extracted.MoveTo(Path.Combine(zipRoot.FullName, $"{sections[0]}{Path.GetExtension(extracted.Name)}"));

        _logger.Debug("Prepared 1-click archive root at {ZipRoot}", zipRoot.FullName);
        return zipRoot;
    }

    /// <summary>
    /// Asks before installing what a link points at and, above all, *where* it goes: a category dropdown
    /// (Characters / Weapons / NPCs / Objects / …), an object dropdown inside that category, and — only when the
    /// chosen character actually has in-game skins — a skin dropdown, because GameBanana cannot express a skin in
    /// a link. Without a selection the Install button stays disabled, so UI/weapon mods and undetected
    /// characters are still installable instead of dead-ending.
    /// </summary>
    private async Task<InstallTarget?> ConfirmInstallAsync(ModPageInfo profile, ModFileInfo? fileInfo,
        SupportedGames game, ICharacter? detectedCharacter, Uri? modUrl, bool offerSkinPicker)
    {
        var lines = new List<string>
        {
            Format("OneClick_Confirm_Mod", "Mod: {0}", profile.ModName ?? "?"),
            Format("OneClick_Confirm_Author", "Author: {0}", profile.AuthorName ?? "?"),
            Format("OneClick_Confirm_Target", "Game: {0}", game)
        };

        if (modUrl is not null)
            lines.Add(Format("OneClick_Confirm_Source", "Source: {0}", modUrl));

        if (fileInfo is not null && (!string.IsNullOrWhiteSpace(fileInfo.AvResult) ||
                                     !string.IsNullOrWhiteSpace(fileInfo.AnalysisResultVerbose)))
        {
            lines.Add(Format("OneClick_Confirm_Scan", "GameBanana scan: {0}{1}",
                fileInfo.AvResult ?? fileInfo.AnalysisResult ?? "?",
                string.IsNullOrWhiteSpace(fileInfo.AnalysisResultVerbose)
                    ? string.Empty
                    : $" — {fileInfo.AnalysisResultVerbose}"));
        }

        // --- pickers (index-based selection: WinUI's SelectedItem setter throws for object items) ---
        var content = new StackPanel { Spacing = 8, MaxWidth = 460 };
        content.Children.Add(new TextBlock
        {
            Text = lines[0],
            TextWrapping = TextWrapping.WrapWholeWords
        });

        foreach (var line in lines.Skip(1))
        {
            content.Children.Add(new TextBlock
            {
                Text = line,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.85
            });
        }

        // Pickers use plain strings as items and keep the values in parallel lists: WinUI fails to marshal
        // custom (private, generic) item types through its ABI, which throws a NullReferenceException from the
        // selector setters instead of anything helpful.
        var categories = _gameService.GetCategories();

        // Category and target sit next to each other; their placeholders say what they are, so labels above
        // them would be redundant.
        var pickerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(pickerRow);

        var (categoryBox, _) = CreatePicker(Format("OneClick_Confirm_Category", "Category"), pickerRow,
            categories.Select(CategoryLabel).ToList());

        // No placeholder on the target picker on purpose: an empty dropdown is the cue that the user has to
        // choose (a label would look like something is already selected).
        var (objectBox, _) = CreatePicker(string.Empty, pickerRow);
        var (skinBox, skinRow) = CreatePicker(string.Empty, content,
            labelAbove: Format("OneClick_Confirm_Skin", "Skin"));
        skinRow.Visibility = Visibility.Collapsed;

        var dialog = new ContentDialog
        {
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Title = Format("OneClick_Confirm_Title", "Install this mod?"),
            Content = content,
            PrimaryButtonText = Format("OneClick_Confirm_Install", "Install"),
            CloseButtonText = Format("OneClick_Confirm_Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false
        };

        var currentObjects = new List<IModdableObject>();
        var currentSkins = new List<ICharacterSkin?>();

        void RefreshObjects()
        {
            var categoryIndex = categoryBox.SelectedIndex;
            var category = categoryIndex >= 0 && categoryIndex < categories.Count ? categories[categoryIndex] : null;

            currentObjects = category is null ? [] : _gameService.GetModdableObjects(category).ToList();
            objectBox.ItemsSource = currentObjects.Select(item => item.DisplayName).ToList();
            objectBox.SelectedIndex = detectedCharacter is null
                ? -1
                : currentObjects.FindIndex(item => item.InternalNameEquals(detectedCharacter.InternalName));

            RefreshSkins();
        }

        void RefreshSkins()
        {
            var objectIndex = objectBox.SelectedIndex;
            var selected = objectIndex >= 0 && objectIndex < currentObjects.Count ? currentObjects[objectIndex] : null;
            var character = selected as ICharacter;
            // Only when the helper will be skipped: it has its own skin selector, so asking twice is redundant.
            var hasSkins = offerSkinPicker && character is not null && character.Skins.Count > 1;

            skinRow.Visibility = hasSkins ? Visibility.Visible : Visibility.Collapsed;

            currentSkins = [null];
            if (hasSkins)
                currentSkins.AddRange(character!.Skins.Where(skin => !skin.IsDefault));

            skinBox.ItemsSource = currentSkins
                .Select(skin => skin?.DisplayName ?? Format("OneClick_Confirm_DefaultSkin", "Default"))
                .ToList();
            skinBox.SelectedIndex = 0;
            dialog.IsPrimaryButtonEnabled = objectBox.SelectedIndex >= 0;
        }

        // Every callback that WinUI can invoke (selection changed, deferred selection) is guarded: an exception
        // inside a dispatcher callback is an unhandled WinRT stowed exception, which kills the process
        // (0xc000027b) instead of surfacing an error. Worst case here is "nothing preselected".
        categoryBox.SelectionChanged += (_, _) => Guarded(RefreshObjects, "refresh the target list");
        objectBox.SelectionChanged += (_, _) => Guarded(RefreshSkins, "refresh the skin list");

        // Selection has to wait until the control is fully realised: a ComboBox raised Loaded still fails with
        // E_POINTER from the selector setters (template/items host not applied yet), so it is deferred one
        // dispatcher tick past Loaded.
        var preselected = false;
        // The ComboBox rejects selection until its template/items host is applied (E_POINTER, which as an
        // unhandled dispatcher exception crashes the app), so this runs after Loaded and retries a few times.
        // Each attempt is guarded: worst case the user picks the target manually.
        var attempts = 0;

        void Preselect()
        {
            if (preselected)
                return;

            try
            {
                var index = detectedCharacter is null
                    ? 0
                    // ICharacter.ModCategory is the ICategory the character belongs to (not the enum).
                    : categories.FindIndex(category => category.Equals(detectedCharacter.ModCategory));

                // With nothing detected (UI/weapon mods, new character, ambiguous name) the character stays
                // unselected and Install is disabled until the user picks one.
                categoryBox.SelectedIndex = index >= 0 ? index : 0;
                RefreshObjects();
                preselected = true;
            }
            catch (Exception e)
            {
                if (++attempts >= 5)
                {
                    _logger.Warning(e,
                        "1-click install dialog could not preselect the target; the user has to choose");
                    return;
                }

                _logger.Debug(e, "1-click install dialog: retrying target preselection ({Attempt})", attempts);
                _ = Task.Delay(200).ContinueWith(_ =>
                    App.MainWindow.DispatcherQueue.TryEnqueue(Preselect));
            }
        }

        categoryBox.Loaded += (_, _) => App.MainWindow.DispatcherQueue.TryEnqueue(Preselect);

        var result = await dialog.ShowAsync();
        _logger.Information("1-click dialog '{Title}' returned {Result}", dialog.Title, result);

        if (result != ContentDialogResult.Primary)
            return null;

        var objectIndex = objectBox.SelectedIndex;
        if (objectIndex < 0 || objectIndex >= currentObjects.Count)
            return null;

        var skinIndex = skinBox.SelectedIndex;
        var skin = skinIndex >= 0 && skinIndex < currentSkins.Count ? currentSkins[skinIndex] : null;

        return new InstallTarget(currentObjects[objectIndex], skin);
    }

    private sealed record InstallTarget(IModdableObject Target, ICharacterSkin? Skin);

    /// <summary>
    /// Runs a dialog callback, logging instead of throwing: WinUI invokes these from its own dispatcher, where an
    /// exception becomes an unhandled stowed exception that terminates the process (observed as an app crash with
    /// exception code 0xc000027b right after the install dialog opened).
    /// </summary>
    private void Guarded(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            _logger.Warning(e, "1-click install dialog could not {What}", what);
        }
    }

    /// <summary>
    /// Adds a labelled picker row to the dialog. Returns the row as well as the box: <c>FrameworkElement.Parent</c>
    /// is null while the panel is not in a visual tree, so the row cannot be recovered from the box (doing so
    /// threw a NullReferenceException and, from a dispatcher callback, crashed the app).
    /// </summary>
    private static (ComboBox Box, StackPanel Row) CreatePicker(string placeholder, StackPanel content,
        List<string>? items = null, string? labelAbove = null)
    {
        var row = new StackPanel { Spacing = 4 };

        if (!string.IsNullOrEmpty(labelAbove))
            row.Children.Add(new TextBlock { Text = labelAbove, FontSize = 12, Opacity = 0.85 });

        var box = new ComboBox
        {
            PlaceholderText = placeholder ?? string.Empty,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 200
        };
        if (items is not null)
            box.ItemsSource = items;

        row.Children.Add(box);
        content.Children.Add(row);

        return (box, row);
    }

    /// <summary>Category label, localized the same way the characters page does it (Category_&lt;name&gt;).</summary>
    private string CategoryLabel(ICategory category) =>
        _localizer.GetLocalizedStringOrDefault("Category_" + category.DisplayNamePlural.Replace(" ", ""))
        ?? category.DisplayNamePlural;

    /// <summary>Warns when the extracted archive contains something executable (see <see cref="ArchiveContentScan"/>).</summary>
    private async Task<bool> ConfirmArchiveContentsAsync(string modTitle, IReadOnlyList<string> executableLikeFiles)
    {
        var shown = executableLikeFiles.Take(10).ToList();
        if (executableLikeFiles.Count > shown.Count)
            shown.Add(Format("OneClick_Warning_More", "…and {0} more", executableLikeFiles.Count - shown.Count));

        return await ShowConfirmDialogAsync(
                Format("OneClick_Warning_Title", "This archive contains executable files"),
                [
                    Format("OneClick_Warning_Message", "{0} contains files that can run code:", modTitle),
                    string.Join(Environment.NewLine, shown)
                ],
                Format("OneClick_Warning_Continue", "Continue anyway"),
                Format("OneClick_Confirm_Cancel", "Cancel"))
            .ConfigureAwait(true);
    }

    private static async Task<bool> ShowConfirmDialogAsync(string title, IReadOnlyList<string> lines,
        string primaryButtonText, string closeButtonText)
    {
        var content = new StackPanel { Spacing = 8, MaxWidth = 460 };

        content.Children.Add(new TextBlock
        {
            Text = lines[0],
            TextWrapping = TextWrapping.WrapWholeWords
        });

        foreach (var line in lines.Skip(1))
        {
            content.Children.Add(new TextBlock
            {
                Text = line,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.85
            });
        }

        var dialog = new ContentDialog
        {
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = closeButtonText,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        Serilog.Log.ForContext<OneClickInstallService>()
            .Information("1-click dialog '{Title}' returned {Result}", title, result);

        return result == ContentDialogResult.Primary;
    }

    /// <summary>Best effort cleanup of the extracted scratch folder when the user backs out.</summary>
    private void TryDeleteTempZipRoot(DirectoryInfo zipRoot)
    {
        try
        {
            var parent = zipRoot.Parent;
            if (parent is not null && parent.Exists && parent.FullName.StartsWith(Path.GetTempPath(),
                    StringComparison.OrdinalIgnoreCase))
                parent.Delete(true);
        }
        catch (Exception e)
        {
            _logger.Debug(e, "Could not clean up the extracted archive after cancelling");
        }
    }

    /// <summary>
    /// Formats a localized string with its arguments, falling back to English when a translation has mismatched
    /// placeholders (translations are editable data — a missing {0} must not turn an install into "failed").
    /// </summary>
    private string Format(string key, string fallback, params object[] args)
    {
        var template = _localizer.GetLocalizedStringOrDefault(key) ?? fallback;

        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            _logger.Warning("1-click string {Key} has mismatched placeholders; using the English text", key);
            return string.Format(fallback, args);
        }
    }

    private void Notify(string titleKey, string message)
    {
        var title = _localizer.GetLocalizedStringOrDefault(titleKey) ?? "GameBanana 1-click install";
        _notificationManager.ShowNotification(title, message, TimeSpan.FromSeconds(15));
    }
}