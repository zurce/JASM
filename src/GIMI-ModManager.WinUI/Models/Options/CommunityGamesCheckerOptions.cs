using GIMI_ModManager.WinUI.Services;
using Newtonsoft.Json;

namespace GIMI_ModManager.WinUI.Models.Options;

/// <summary>
/// App-scoped (shared across all games) state for the community-assets update checker.
/// The community assets repo at %LocalAppData%\JASM\CommunityGames is a single shared
/// clone, so its "last dismissed update" marker is app-wide rather than per-game.
/// </summary>
public class CommunityGamesCheckerOptions
{
    [JsonIgnore] public const string Key = "CommunityGamesChecker";
    [JsonIgnore] public const SettingScope Scope = SettingScope.App;

    /// <summary>The remote commit SHA the user last dismissed (badge suppressed for this SHA).</summary>
    public string? IgnoredCommitSha { get; set; }
}