using System.Text.RegularExpressions;
using GIMI_ModManager.Core.Services.GameBanana.Models;

namespace GIMI_ModManager.Core.Services.Protocol;

/// <summary>
/// A validated GameBanana 1-click install request, as handed to the registered custom URI scheme.
/// </summary>
/// <param name="Identifier">The submission + file the link points at.</param>
/// <param name="ArchiveUrl">The archive URL embedded in the link (always a GameBanana host).</param>
/// <param name="Raw">The original, trimmed argument.</param>
public sealed record OneClickInstallRequest(GbModFileIdentifier Identifier, Uri ArchiveUrl, string Raw)
{
    public GbModId ModId => Identifier.ModId;
    public GbModFileId ModFileId => Identifier.ModFileId;
    public bool IsTool => Identifier.IsTool;
}

/// <summary>
/// Parser for GameBanana's 1-click installer links.
///
/// GameBanana builds these itself and hands them to the OS through a custom URL scheme
/// (see <see href="https://gamebanana.com/wikis/1999">1-Click Mod Installers</see>). The exact shape,
/// verified against GameBanana's own data (<c>apiv11/Mod/&lt;id&gt;/DownloadPage</c> →
/// <c>_aFiles[]._aModManagerIntegrations[]._sDownloadUrl</c>):
/// <code>
/// jasm-plus:https://gamebanana.com/mmdl/1831455,Mod,691863
/// </code>
/// i.e. <c>{scheme}:{https archive url}, {Mod|Tool}, {submission row id}</c>.
///
/// This type is deliberately the whole trust boundary: a protocol handler is reachable from any web page, so
/// anything that is not exactly this shape — a different host (especially <c>http</c>), a different scheme,
/// extra path segments, junk before/after — is rejected rather than sanitised. Nothing downstream may fetch
/// a URL that did not come through here.
/// </summary>
public static class OneClickUri
{
    /// <summary>Scheme a release build registers with GameBanana (authors opt out with <c>.disable_gb1click_jasm-plus</c>).</summary>
    public const string DefaultScheme = "jasm-plus";

    /// <summary>Only GameBanana may trigger an install; these are the hosts its links use.</summary>
    private static readonly string[] AllowedHosts = ["gamebanana.com", "www.gamebanana.com"];

    /// <summary>Sanity cap: real links are well under 100 chars; anything longer is hostile or broken.</summary>
    public const int MaxArgumentLength = 512;

    private static readonly Regex Pattern = new(
        @"^(?<scheme>[A-Za-z][A-Za-z0-9+.\-]*):(?<url>https://(?<host>[A-Za-z0-9.\-]+)/mmdl/(?<fileId>\d+)),(?<type>Mod|Tool),(?<modId>\d+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// Tries to parse a raw command line argument.
    /// Windows may hand over the argument padded with whitespace, so it is trimmed first.
    /// </summary>
    /// <param name="rawArgument">The raw argument, e.g. <c>argv[1]</c>. Null/empty is simply "not ours".</param>
    /// <param name="request">The parsed request when this returns true.</param>
    /// <param name="scheme">Expected scheme; defaults to <see cref="DefaultScheme"/>. Overridable for dev/test.</param>
    public static bool TryParse(string? rawArgument, out OneClickInstallRequest? request, string scheme = DefaultScheme)
    {
        request = null;

        var raw = Normalize(rawArgument);
        if (string.IsNullOrEmpty(raw) || raw.Length > MaxArgumentLength)
            return false;

        var match = Pattern.Match(raw);
        if (!match.Success)
            return false;

        if (!string.Equals(match.Groups["scheme"].Value, scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        var host = match.Groups["host"].Value;
        if (!AllowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            return false;

        if (!int.TryParse(match.Groups["fileId"].Value, out var fileId) ||
            !int.TryParse(match.Groups["modId"].Value, out var modId) ||
            fileId <= 0 || modId <= 0)
            return false;

        if (!Uri.TryCreate(match.Groups["url"].Value, UriKind.Absolute, out var archiveUrl))
            return false;

        var isTool = match.Groups["type"].Value.Equals("Tool", StringComparison.OrdinalIgnoreCase);

        request = new OneClickInstallRequest(
            new GbModFileIdentifier(new GbModId(modId), new GbModFileId(fileId), isTool),
            archiveUrl,
            raw);

        return true;
    }

    /// <summary>
    /// Normalises a raw argument: Windows may pad it with whitespace and, depending on how the process was
    /// started (ShellExecute vs a command line built by a browser), may also wrap the whole argument in
    /// quotes — <c>"jasm-plus:https://…"</c>. Both forms are the same link.
    /// </summary>
    internal static string? Normalize(string? rawArgument)
    {
        var raw = rawArgument?.Trim();
        if (string.IsNullOrEmpty(raw))
            return raw;

        if (raw.Length > 1 && raw[0] == '"' && raw[^1] == '"')
            raw = raw[1..^1].Trim();

        return raw;
    }

    /// <summary>Builds a link. Used by tests and dev tooling — production only ever parses.</summary>
    public static string Build(int fileId, int modId, bool isTool = false, string scheme = DefaultScheme)
        => $"{scheme}:https://gamebanana.com/mmdl/{fileId},{(isTool ? "Tool" : "Mod")},{modId}";
}