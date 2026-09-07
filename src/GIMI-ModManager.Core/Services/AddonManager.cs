using GIMI_ModManager.Core.Contracts.Entities;
using GIMI_ModManager.Core.Entities.Mods.Contract;
using GIMI_ModManager.Core.Helpers;
using Serilog;

namespace GIMI_ModManager.Core.Services;

/// <summary>
/// Add-on support for mods installed with multiple files ("multi install").
/// Add-ons nest one level deep inside their parent root folder; disabling a root folder
/// effectively disables its add-ons too. Unlike variants, add-ons toggle independently
/// of each other and are all enabled by default:
/// <code>
/// coolmod/
///   .JASM_ModConfig.json      (variants + addons arrays)
///   mainmod/...               (exclusive variant root, active)
///     addonA/...              (add-on, enabled)
///     DISABLED_addonB/...     (add-on, disabled by the user later)
///   DISABLED_other/...        (exclusive variant root, inactive with its add-ons)
/// </code>
/// The disk (DISABLED_ prefix) is the source of truth for enabled state; the config mirrors it
/// and defines WHICH subfolders are add-ons (tracker only sees top-level folders, so nested
/// add-ons never appear as separate mods).
/// </summary>
public static class AddonManager
{
    private static readonly ILogger Logger = Log.ForContext(typeof(AddonManager));

    /// <summary>
    /// Detects the add-ons of a mod. A mod supports add-ons when its .JASM_ModConfig.json has a
    /// non-empty addons array; each entry is matched one level deep (inside the root folders).
    /// Returns null when the mod does not support add-ons.
    /// </summary>
    public static IReadOnlyList<ModAddon>? DetectAddons(ISkinMod mod)
    {
        if (!mod.Settings.TryGetSettings(out var settings) || settings.Addons is not { Count: > 0 })
            return null;

        var root = new DirectoryInfo(mod.FullPath);
        var matchedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var result = new List<ModAddon>();
        foreach (var configEntry in settings.Addons)
        {
            // Match one level deep, or by the unprefixed form of the stored name.
            var folder = root.EnumerateDirectories()
                .SelectMany(r => r.EnumerateDirectories())
                .FirstOrDefault(d =>
                    !matchedDirs.Contains(d.FullName) &&
                    (d.Name.Equals(configEntry.FolderName, StringComparison.OrdinalIgnoreCase) ||
                    ModFolderHelpers.FolderNameEquals(d.Name, configEntry.FolderName) ||
                    ModFolderHelpers.FolderNameEquals(d.Name, configEntry.Name)));

            if (folder is null)
            {
                Logger.Warning("Add-on folder '{Folder}' of mod '{Mod}' is missing on disk",
                    configEntry.FolderName, mod.GetDisplayName());
                continue;
            }
            matchedDirs.Add(folder.FullName);

            var enabled = !ModFolderHelpers.FolderHasDisabledPrefix(folder.Name);

            result.Add(new ModAddon(
                Name: string.IsNullOrWhiteSpace(configEntry.Name)
                    ? ModFolderHelpers.GetFolderNameWithoutDisabledPrefix(folder.Name)
                    : configEntry.Name,
                FolderName: folder.Name,
                Enabled: enabled));
        }

        return result.Count == 0
            ? null
            : result.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Builds the addons config array for a freshly installed multi-install mod:
    /// every archive enabled. Names are the bare archive base names.</summary>
    public static IReadOnlyList<ModAddon> CreateInitialAddons(IReadOnlyCollection<string> addonNames)
    {
        return addonNames
            .Select(name => new ModAddon(
                Name: name,
                FolderName: name,
                Enabled: true))
            .ToList();
    }
}
