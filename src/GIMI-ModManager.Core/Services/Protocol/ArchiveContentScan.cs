namespace GIMI_ModManager.Core.Services.Protocol;

/// <summary>
/// Scans an extracted mod archive for files that can execute code, so a 1-click install (or any install from a
/// web-triggered link) can warn before handing it to the installer.
///
/// Mods for these games are 3DMigoto mods — shaders, textures and ini files (.ini/.buf/.ib/.dds/…) and never
/// need to run anything, so the presence of an executable is worth an explicit prompt. This is the same advice
/// GameBanana gives to integrated installers ("scan the decompressed archive … add a continue prompt").
/// </summary>
public static class ArchiveContentScan
{
    /// <summary>
    /// Extensions that can run code on Windows. GameBanana's own scan result is shown alongside this — this
    /// check exists because the archive is downloaded and extracted by us.
    /// </summary>
    private static readonly string[] ExecutableLikeExtensions =
    [
        ".exe", ".com", ".scr", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".dll"
    ];

    /// <summary>
    /// Paths (relative to <paramref name="rootPath"/>) of executable-like files, or empty when there are none.
    /// Never throws: a folder that cannot be read is treated as "nothing found" — a broken scan must not block
    /// an install, and the caller shows GameBanana's own verdict regardless.
    /// </summary>
    public static IReadOnlyList<string> FindExecutableLikeFiles(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return [];

        try
        {
            return Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                .Where(path => ExecutableLikeExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Select(path => Path.GetRelativePath(rootPath, path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }
}