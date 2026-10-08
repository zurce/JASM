using System.Text.Json.Serialization;
using GIMI_ModManager.Core.Services.Protocol;
using GIMI_ModManager.WinUI.Services;

namespace GIMI_ModManager.WinUI.Models.Settings;

/// <summary>
/// App-scoped (shared across all games) settings for GameBanana 1-click installs.
///
/// The custom URL scheme is registered once per machine/user, and a single registration serves every game,
/// so these settings cannot live in the per-game settings file.
/// </summary>
public class OneClickInstallSettings
{
    [JsonIgnore] public const string Key = "OneClickInstall";
    [JsonIgnore] public const SettingScope Scope = SettingScope.App;

    /// <summary>
    /// Registers the custom URL scheme so links clicked on GameBanana reach JASM+. On by default; turning it
    /// off removes the registration again.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Scheme to register and accept. Kept configurable so a build can be pointed at a scheme GameBanana already
    /// knows while integration is being set up; production uses <see cref="OneClickUri.DefaultScheme"/>.
    /// </summary>
    public string Scheme { get; set; } = OneClickUri.DefaultScheme;

    /// <summary>
    /// Skip the pre-install confirmation and install as soon as the link is handled. Off by default: a link is
    /// triggered from a web page, so installing without asking should be an explicit choice. The archive content
    /// warning is not affected by this.
    /// </summary>
    public bool InstallWithoutConfirmation { get; set; }
}