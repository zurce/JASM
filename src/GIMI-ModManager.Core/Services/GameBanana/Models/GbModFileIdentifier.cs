namespace GIMI_ModManager.Core.Services.GameBanana.Models;

/// <summary>
/// Represents a unique identifier for a mod file on GameBanana.
/// <param name="ModId">The submission row id (the numeric part of the profile URL).</param>
/// <param name="ModFileId">The file row id.</param>
/// <param name="IsTool">True when the submission is a <em>Tool</em> (gamebanana.com/tools/&lt;id&gt;),
/// not a <em>Mod</em>. Tools and mods share the same numeric id space but are different
/// submissions, and GameBanana serves their file lists from different API namespaces
/// (<c>apiv11/Mod/&lt;id&gt;/DownloadPage</c> vs <c>apiv11/Tool/&lt;id&gt;/DownloadPage</c>).</param>
/// </summary>
public record GbModFileIdentifier(GbModId ModId, GbModFileId ModFileId, bool IsTool = false);