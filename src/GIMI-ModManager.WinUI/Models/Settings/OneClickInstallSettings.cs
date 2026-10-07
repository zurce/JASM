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
    /// Scheme to register and accept. Only ever changed for development (pointing a scheme that GameBanana
    /// already knows, e.g. the Firefox dev harness) — production uses <see cref="OneClickUri.DefaultScheme"/>.
    /// </summary>
    public string Scheme { get; set; } = OneClickUri.DefaultScheme;

    /// <summary>
    /// Development-only: accept the <c>?game=&amp;character=&amp;skin=&amp;autostart=</c> suffix on a link so the
    /// dev harness can reproduce cases GameBanana cannot express (skins above all). Off by default, and the
    /// suffix is rejected outright when off.
    /// </summary>
    public bool AllowDevOptions { get; set; }
}