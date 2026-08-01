using System.Text.RegularExpressions;
using GIMI_ModManager.Core.GamesService;

namespace GIMI_ModManager.Core.Services;

/// <summary>
/// Describes a recognized XXMI installation for a given JASM-supported game.
/// XXMI (SpectrumQT's centralized model injector) manages per-game "importer" folders
/// under the XXMI Launcher install location, e.g. ...\XXMI Launcher\GIMI.
/// </summary>
public sealed record XxmiInstallation
{
    /// <summary>The XXMI importer folder (e.g. C:\Users\...\XXMI Launcher\GIMI).</summary>
    public required string RootFolderPath { get; init; }

    /// <summary>The XXMI game/variant folder name (e.g. "GIMI").</summary>
    public required string GameIdentifier { get; init; }

    /// <summary>The standard XXMI mods folder: RootFolderPath\Mods.</summary>
    public required string ModsFolderPath { get; init; }
}

/// <summary>
/// Detects XXMI installations from a model-importer (3DMigoto) root path and
/// describes their standard folder layout. Designed to be static/side-effect-free
/// so both the Settings and Startup flows can share one detection rule.
/// </summary>
public static class XxmiInstallationDetector
{
    /// <summary>
    /// The known game importer folder names XXMI uses (mirrors JASM's SupportedGames).
    /// GIMI=Genshin, SRMI=Honkai Star Rail, WWMI=Wuthering Waves, ZZMI=ZZZ, EFMI=Endfield.
    /// Note: XXMI also supports HIMI (Honkai Impact 3rd) which JASM does not ship.
    /// </summary>
    public static readonly IReadOnlySet<string> KnownGameIdentifiers =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GIMI", "SRMI", "WWMI", "ZZMI", "EFMI" };

    /// <summary>
    /// Maps a JASM <see cref="SupportedGames"/> to its XXMI game importer identifier.
    /// Returns <c>null</c> when there is no XXMI counterpart for the game.
    /// </summary>
    public static string? GetXxmiGameIdentifier(SupportedGames game) => game switch
    {
        SupportedGames.Genshin => "GIMI",
        SupportedGames.Honkai => "SRMI",
        SupportedGames.WuWa => "WWMI",
        SupportedGames.ZZZ => "ZZMI",
        SupportedGames.Endfield => "EFMI",
        _ => null
    };

    private const string LoaderLinePattern = @"^\s*loader\s*=\s*XXMI\s+Launcher\.exe\s*$";
    private const string D3dxIniFileName = "d3dx.ini";

    /// <summary>
    /// Attempts to detect whether <paramref name="rootFolderPath"/> is an XXMI-managed
    /// importer folder for the expected game identifier, without requiring that XXMI be
    /// installed (i.e. the folder may declare the XXMI loader or sit under "XXMI Launcher").
    /// Returns <c>null</c> if the path is not recognized. See <see cref="TryDetect"/>.
    /// </summary>
    public static XxmiInstallation? TryDetect(string? rootFolderPath) => TryDetect(rootFolderPath, expectedIdentifier: null);

    /// <summary>
    /// Attempts to detect whether <paramref name="rootFolderPath"/> is an XXMI-managed
    /// importer folder for <paramref name="expectedIdentifier"/> (e.g. "GIMI"). When a
    /// non-null expected identifier is supplied, only a folder whose XXMI game identifier
    /// matches is recognized — so picking e.g. the GIMI folder while configuring Star Rail
    /// (expected "SRMI") is rejected.
    /// </summary>
    /// <remarks>
    /// Detection rules (any recognized path segment match is sufficient):
    /// 1. The folder is named after a known XXMI game identifier (GIMI/SRMI/WWMI/ZZMI/EFMI)
    ///    AND its effective root sits under a "XXMI Launcher" directory, OR
    /// 2. The folder's d3dx.ini declares <c>loader = XXMI Launcher.exe</c>.
    /// The standard XXMI mods folder is always RootFolder\Mods when detected.
    /// When <paramref name="expectedIdentifier"/> is provided it must match the detected
    /// identifier (case-insensitively).
    /// </remarks>
    public static XxmiInstallation? TryDetect(string? rootFolderPath, string? expectedIdentifier)
    {
        if (string.IsNullOrWhiteSpace(rootFolderPath))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(rootFolderPath);
            var dir = new DirectoryInfo(fullPath);
            if (!dir.Exists)
                return null;

            // A folder is only treated as an XXMI importer folder when it has an
            // expected "Mods" subfolder (that is the layout XXMI always creates) OR it
            // declares the XXMI loader in its d3dx.ini. We require Mods to guarantee we
            // don't lock the user into a folder XXMI does not really manage.
            var modsFolder = Path.Combine(dir.FullName, "Mods");

            var folderName = dir.Name;
            var isKnownIdentifier = KnownGameIdentifiers.Contains(folderName);
            var underXxmiLauncher = IsUnderXxmiLauncher(dir);
            var declaresXxmiLoader = DeclaresXxmiLoader(dir);

            if (!isKnownIdentifier && !declaresXxmiLoader)
                return null;
            if (!underXxmiLauncher && !declaresXxmiLoader)
                return null;
            if (!ModsFolderExists(modsFolder))
                return null;

            var identifier = isKnownIdentifier ? folderName : folderName.ToUpperInvariant();

            // Enforce the expected game when supplied, so a wrong game pairing is rejected.
            if (!string.IsNullOrWhiteSpace(expectedIdentifier) &&
                !string.Equals(identifier, expectedIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new XxmiInstallation
            {
                RootFolderPath = dir.FullName,
                GameIdentifier = identifier,
                ModsFolderPath = modsFolder
            };
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// The default XXMI launcher executable name.
    /// </summary>
    public const string LauncherExeName = "XXMI Launcher.exe";

    /// <summary>
    /// Attempts to locate the XXMI Launcher executable. Prefers the well-known install
    /// location (<c>%APPDATA%\XXMI Launcher\Resources\Bin\XXMI Launcher.exe</c>); falls
    /// back to the local-app-data variant if present.
    /// </summary>
    public static string? TryResolveLauncherExe()
    {
        const string subFolder = @"XXMI Launcher\Resources\Bin";

        string[] baseFolders =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        foreach (var baseFolder in baseFolders)
        {
            if (string.IsNullOrWhiteSpace(baseFolder))
                continue;

            var candidate = Path.Combine(baseFolder, subFolder, LauncherExeName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static bool ModsFolderExists(string modsFolder) =>
        Directory.Exists(modsFolder) && (Directory.Exists(Path.Combine(modsFolder, "character")) ||
                                         Directory.EnumerateFileSystemEntries(modsFolder).Any());

    private static bool IsUnderXxmiLauncher(DirectoryInfo? dir)
    {
        // Walk up to at most 3 levels looking for a directory named "XXMI Launcher".
        var current = dir;
        for (var i = 0; i < 3 && current is not null; i++)
        {
            if (current.Parent is null)
                break;
            if (string.Equals(current.Parent.Name, "XXMI Launcher", StringComparison.OrdinalIgnoreCase))
                return true;
            current = current.Parent;
        }

        return false;
    }

    private static bool DeclaresXxmiLoader(DirectoryInfo dir)
    {
        var d3dxIni = Path.Combine(dir.FullName, D3dxIniFileName);
        if (!File.Exists(d3dxIni))
            return false;

        try
        {
            using var reader = new StreamReader(d3dxIni);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Length > 0 && !line.TrimStart().StartsWith(';') && !line.TrimStart().StartsWith('#'))
                {
                    if (Regex.IsMatch(line, LoaderLinePattern, RegexOptions.IgnoreCase))
                        return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }
}
