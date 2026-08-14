using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Models.Options;
using LibGit2Sharp;
using Serilog;

namespace GIMI_ModManager.WinUI.Services.AppManagement.Updating;

/// <summary>
/// Background checker that tells the user when the community assets repo
/// (cloned to <c>%LocalAppData%\JASM\CommunityGames</c>) has new commits upstream.
/// Mirrors the app-update <see cref="UpdateChecker"/> pattern: runs on a timer and
/// raises an event that <see cref="ViewModels.ShellViewModel"/> uses to light up the
/// Settings nav <c>InfoBadge</c>, so the user knows a manual Update in Settings will
/// bring in new community resources.
/// </summary>
/// <remarks>
/// The check is strictly read-only against the working tree: it only <c>Fetch</c>es the
/// remote into remote-tracking refs (never <c>Pull</c>/merge/reset), so a background tick
/// can never mutate the user's checked-out community assets or conflict with in-flight
/// mod reads. The actual pull remains the existing manual Settings flow.
/// </remarks>
public sealed class CommunityGamesUpdateChecker
{
    private readonly ILogger _logger;
    private readonly ILocalSettingsService _localSettingsService;

    private CancellationTokenSource? _cancellationTokenSource;
    private string? _ignoredCommitSha;

    private const string CommunityRepoRemote = "origin";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(2);

    /// <summary>Raised when the community repo is configured for the selected game and has new upstream commits.</summary>
    public event EventHandler<EventArgs>? NewCommitAvailable;

    /// <summary>Raised when the badge should be cleared (no pending update / dismissed).</summary>
    public event EventHandler<EventArgs>? NoNewCommitAvailable;

    /// <summary>True when the current check found (and hasn't dismissed) a pending community update.</summary>
    public bool IsUpdateAvailable { get; private set; }

    public CommunityGamesUpdateChecker(
        ILogger logger,
        ILocalSettingsService localSettingsService)
    {
        _logger = logger.ForContext<CommunityGamesUpdateChecker>();
        _localSettingsService = localSettingsService;
    }

    public async Task InitializeAsync()
    {
        var options = await _localSettingsService
            .ReadOrCreateSettingAsync<CommunityGamesCheckerOptions>(CommunityGamesCheckerOptions.Key,
                CommunityGamesCheckerOptions.Scope);

        _ignoredCommitSha = options.IgnoredCommitSha;

        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        Task.Factory.StartNew(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await RunCheckAsync(token).ConfigureAwait(false);
                    await Task.Delay(CheckInterval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to check for community games updates. Retrying in {Interval}...", CheckInterval);
                    try
                    {
                        await Task.Delay(CheckInterval, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    /// <summary>Dismiss the current pending update so the badge does not nag until the next remote change.</summary>
    public async Task IgnoreCurrentUpdateAsync()
    {
        var (communityDir, _) = await GetCommunitySourceAsync().ConfigureAwait(false);

        if (communityDir is not null && Repository.IsValid(communityDir))
        {
            var (_, remoteTipSha) = await Task.Run(() => GetCommitState(communityDir)).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(remoteTipSha))
                _ignoredCommitSha = remoteTipSha;
        }

        var options = await _localSettingsService
            .ReadOrCreateSettingAsync<CommunityGamesCheckerOptions>(CommunityGamesCheckerOptions.Key,
                CommunityGamesCheckerOptions.Scope);
        options.IgnoredCommitSha = _ignoredCommitSha;
        await _localSettingsService.SaveSettingAsync(CommunityGamesCheckerOptions.Key, options,
            CommunityGamesCheckerOptions.Scope).ConfigureAwait(false);

        IsUpdateAvailable = false;
        NoNewCommitAvailable?.Invoke(this, EventArgs.Empty);
    }

    public void CancelAndStop()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private async Task RunCheckAsync(CancellationToken cancellationToken)
    {
        var withUpdate = await IsUpdateAvailableAsync(cancellationToken).ConfigureAwait(false);

        if (withUpdate && !IsUpdateAvailable)
        {
            IsUpdateAvailable = true;
            NewCommitAvailable?.Invoke(this, EventArgs.Empty);
        }
        else if (!withUpdate && IsUpdateAvailable)
        {
            IsUpdateAvailable = false;
            NoNewCommitAvailable?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<bool> IsUpdateAvailableAsync(CancellationToken cancellationToken)
    {
        var (communityDir, repoUrl) = await GetCommunitySourceAsync().ConfigureAwait(false);

        // Only relevant when the selected game uses community assets AND the repo is configured.
        if (string.IsNullOrWhiteSpace(repoUrl) || string.IsNullOrWhiteSpace(communityDir))
            return false;

        cancellationToken.ThrowIfCancellationRequested();

        // Not cloned yet (first-time setup) -> nothing to compare; the badge is premature.
        if (!Directory.Exists(communityDir) || !Repository.IsValid(communityDir))
            return false;

        var (localSha, remoteSha) = await Task.Run(() => GetCommitState(communityDir), cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(localSha) || string.IsNullOrEmpty(remoteSha))
            return false;

        if (localSha == remoteSha)
            return false;

        // Dismissed commit still pending upstream? Keep it dismissed (don't nag).
        if (!string.IsNullOrEmpty(_ignoredCommitSha) && _ignoredCommitSha == remoteSha)
            return false;

        return true;
    }

    /// <summary>
    /// Returns the local working-tree HEAD commit SHA and the remote default-branch HEAD SHA,
    /// or <c>(null, null)</c> when they cannot be resolved.
    /// </summary>
    private static (string? LocalSha, string? RemoteSha) GetCommitState(string communityDir)
    {
        try
        {
            using var repo = new Repository(communityDir);

            // Only fetch into remote-tracking refs; never touch the worktree/local branch.
            foreach (var remote in repo.Network.Remotes)
            {
                var refSpecs = remote.FetchRefSpecs.Select(r => r.Specification);
                Commands.Fetch(repo, remote.Name, refSpecs, new FetchOptions
                {
                    TagFetchMode = TagFetchMode.None
                }, "JASM community assets update check");
            }

            var localSha = repo.Info.IsHeadUnborn ? null : repo.Head.Tip?.Sha;

            var remoteSha = ResolveRemoteHeadSha(repo, CommunityRepoRemote) ?? ResolveDefaultBranchSha(repo);

            return (localSha, remoteSha);
        }
        catch (Exception e)
        {
            // Log but do not throw — a transient network/repo failure should not crash the loop.
            Log.Error(e, "Failed to compare community games repo state at {Path}", communityDir);
            return (null, null);
        }
    }

    /// <summary>Resolves <c>refs/remotes/origin/HEAD</c> (symbolic) to the SHA of the remote default branch.</summary>
    private static string? ResolveRemoteHeadSha(Repository repo, string remoteName)
    {
        try
        {
            var refName = $"refs/remotes/{remoteName}/HEAD";
            var headRef = (SymbolicReference?)repo.Refs[refName];
            var direct = headRef?.ResolveToDirectReference();
            return direct?.TargetIdentifier is { Length: 40 } sha ? sha : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Fallback: the tip SHA of the newest branch under <c>refs/remotes/origin/</c>.</summary>
    private static string? ResolveDefaultBranchSha(Repository repo)
    {
        try
        {
            return repo.Branches
                .Where(b => b.IsRemote && !b.FriendlyName.EndsWith("/HEAD", StringComparison.Ordinal))
                .Select(b => b.Tip?.Sha)
                .FirstOrDefault(sha => !string.IsNullOrEmpty(sha));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns (community-dir, repo-url) when the selected game uses the community source,
    /// else (null, null). The community repo location matches <c>SettingsViewModel.UpdateCommunityGamesAsync</c>.
    /// </summary>
    private async Task<(string? CommunityDir, string? RepoUrl)> GetCommunitySourceAsync()
    {
        var options = await _localSettingsService
            .ReadSettingAsync<ModManagerOptions>(ModManagerOptions.Section).ConfigureAwait(false);

        if (options?.GameSource != GameSource.Community)
            return (null, null);

        var repoUrl = string.IsNullOrWhiteSpace(options.CommunityRepoUrl)
            ? "https://github.com/zurce/JASM-Community-Resources"
            : options.CommunityRepoUrl;

        var communityDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JASM", "CommunityGames");

        return (communityDir, repoUrl);
    }
}