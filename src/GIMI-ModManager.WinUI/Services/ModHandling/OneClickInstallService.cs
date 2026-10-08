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
            var profile = await _gameBananaCoreService
                .GetModProfileAsync(new GbModId(request.ModId), ct)
                .ConfigureAwait(true);

            if (profile is null)
            {
                Notify("OneClick_Failed_Title",
                    Format("OneClick_ModNotFound_Message", "GameBanana did not return any info for mod {0}.",
                        request.ModId));
                return;
            }

            var game = await ResolveGameAsync(profile.GameBananaGameId).ConfigureAwait(true);
            if (game is null)
            {
                Notify("OneClick_UnsupportedGame_Title",
                    Format("OneClick_UnsupportedGame_Message", "This mod is for {0}, which JASM+ does not support.",
                        profile.GameBananaGameName is { Length: > 0 } name ? name : "a game"));
                return;
            }

            var selectedGame = await _selectedGameService.GetSelectedGameAsync().ConfigureAwait(true);
            if (!string.Equals(game.ToString(), selectedGame, StringComparison.OrdinalIgnoreCase))
            {
                // Follow-up: a "switch to <game> and install?" prompt. For now, be explicit rather than
                // silently switching the user's whole UI context.
                Notify("OneClick_WrongGame_Title",
                    Format("OneClick_WrongGame_Message", "This mod is for {0}. Switch JASM+ to {0} and click the link again.",
                        game.ToString()));
                return;
            }

            var character = ResolveCharacter(profile.GameBananaCategoryName);
            if (character is null)
            {
                Notify("OneClick_UnknownTarget_Title",
                    Format("OneClick_UnknownTarget_Message",
                        "Could not find a character called \"{0}\" in JASM+. Install the mod from GameBanana manually.",
                        profile.GameBananaCategoryName ?? "?"));
                return;
            }

            var modList = _skinManagerService.GetCharacterModList(character);
            var modUrl = profile.ModPageUrl;
            var modTitle = profile.ModName ?? character.DisplayName;
            var fileInfo = profile.Files?.FirstOrDefault(file => file.FileId == request.ModFileId.ToString());

            var settings = await _oneClickLaunchService.GetSettingsAsync().ConfigureAwait(true);

            // A link comes from a web page, so installing is confirmed unless the user turned that off.
            if (!settings.InstallWithoutConfirmation &&
                !await ConfirmInstallAsync(profile, fileInfo, game.Value, character, modUrl).ConfigureAwait(true))
            {
                _logger.Information("1-click install for mod {ModId} was cancelled before downloading", request.ModId);
                return;
            }

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

            await _modInstallerService
                .StartModInstallationAsync(zipRoot, modList, inGameSkin: null,
                    setup: options => options.ModUrl = modUrl)
                .ConfigureAwait(true);
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
    /// Asks before downloading and installing what a link points at: what the mod is, who made it, where it goes
    /// and what GameBanana's own scan said. A URL scheme is triggerable from any web page, so this is the point
    /// where the user decides — unless they turned confirmation off in Settings.
    /// </summary>
    private async Task<bool> ConfirmInstallAsync(ModPageInfo profile, ModFileInfo? fileInfo, SupportedGames game,
        ICharacter character, Uri? modUrl)
    {
        var lines = new List<string>
        {
            Format("OneClick_Confirm_Mod", "Mod: {0}", profile.ModName ?? "?"),
            Format("OneClick_Confirm_Author", "Author: {0}", profile.AuthorName ?? "?"),
            Format("OneClick_Confirm_Target", "Install for: {0} — {1}", game, character.DisplayName)
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

        lines.Add(Format("OneClick_Confirm_Note", "It will be downloaded and the Mod Installer will open."));

        return await ShowConfirmDialogAsync(
                Format("OneClick_Confirm_Title", "Install this mod?"),
                lines,
                Format("OneClick_Confirm_Install", "Install"),
                Format("OneClick_Confirm_Cancel", "Cancel"))
            .ConfigureAwait(true);
    }

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

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
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

    private string Format(string key, string fallback, params object[] args) =>
        string.Format(_localizer.GetLocalizedStringOrDefault(key) ?? fallback, args);

    private void Notify(string titleKey, string message)
    {
        var title = _localizer.GetLocalizedStringOrDefault(titleKey) ?? "GameBanana 1-click install";
        _notificationManager.ShowNotification(title, message, TimeSpan.FromSeconds(15));
    }
}