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
    private readonly NotificationManager _notificationManager;
    private readonly ILanguageLocalizer _localizer;
    private readonly ILogger _logger;

    public OneClickInstallService(GameBananaCoreService gameBananaCoreService,
        IGameService gameService,
        ISkinManagerService skinManagerService,
        ModInstallerService modInstallerService,
        ArchiveService archiveService,
        SelectedGameService selectedGameService,
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

            // Dev harness: pretend the submission belongs to another game (to exercise the wrong-game path).
            var gameRowId = request.DevOptions?.GameBananaGameRowId ?? profile.GameBananaGameId;
            var game = await ResolveGameAsync(gameRowId).ConfigureAwait(true);
            if (game is null)
            {
                Notify("OneClick_UnsupportedGame_Title",
                    _localizer.GetLocalizedStringOrDefault("OneClick_UnsupportedGame_Message")
                    ?? $"This mod is for a game JASM+ does not support{(
                        profile.GameBananaGameName is { Length: > 0 } name ? $" ({name})" : string.Empty)}.");
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

            // Dev harness: force a character (or a deliberately wrong one) instead of the category-derived one.
            var characterName = request.DevOptions?.CharacterName is { Length: > 0 } devCharacter
                ? devCharacter
                : profile.GameBananaCategoryName;

            var character = ResolveCharacter(characterName);
            if (character is null)
            {
                Notify("OneClick_UnknownTarget_Title",
                    Format("OneClick_UnknownTarget_Message",
                        "Could not find a character called \"{0}\" in JASM+. Install the mod from GameBanana manually.",
                        characterName ?? "?"));
                return;
            }

            var modList = _skinManagerService.GetCharacterModList(character);
            var modUrl = profile.ModPageUrl;

            var archivePath = await _gameBananaCoreService
                .DownloadModAsync(request.Identifier, progress: null, ct)
                .ConfigureAwait(true);

            var zipRoot = ExtractToArchiveRoot(archivePath);

            _logger.Information("Opening the Mod Installer for {ModName} (mod {ModId}, file {FileId})",
                profile.ModName ?? character.DisplayName, request.ModId, request.ModFileId);

            await _modInstallerService
                .StartModInstallationAsync(zipRoot, modList, inGameSkin: null,
                    setup: options =>
                    {
                        options.ModUrl = modUrl;
                        // Dev harness only (never set by a GameBanana link in production).
                        options.PreferredSkinInternalName = request.DevOptions?.SkinInternalName;
                        options.AutoInstall = request.DevOptions?.AutoInstall ?? false;
                    })
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

    private string Format(string key, string fallback, params object[] args) =>
        string.Format(_localizer.GetLocalizedStringOrDefault(key) ?? fallback, args);

    private void Notify(string titleKey, string message)
    {
        var title = _localizer.GetLocalizedStringOrDefault(titleKey) ?? "GameBanana 1-click install";
        _notificationManager.ShowNotification(title, message, TimeSpan.FromSeconds(15));
    }
}