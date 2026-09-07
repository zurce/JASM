using GIMI_ModManager.Core.Contracts.Entities;
using GIMI_ModManager.Core.Entities.Mods.Contract;
using GIMI_ModManager.Core.Helpers;
using Serilog;

namespace GIMI_ModManager.Core.Services;

/// <summary>
/// Add-on support for mods installed with multiple files ("multi install").
/// Add-ons live flat side by side with the variant roots under the top folder and
/// belong to the main mod, not to any variant. Unlike variants, add-ons toggle
/// independently of each other and are all enabled by default:
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
            // Match by stored folder name, or by the unprefixed form of it (state may have changed).
            var folder = root.EnumerateDirectories()
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

    /// <summary>
    /// Enables or disables one add-on folder, then re-mirrors the on-disk state into
    /// .JASM_ModConfig.json. Enabling a whole mod folder is the caller's job.
    /// </summary>
    public static async Task SetAddonEnabledAsync(ISkinMod parentMod, string folderName, bool enable)
    {
        var location = FindAddonDirectory(parentMod, folderName);
        if (location is null)
            throw new InvalidOperationException(
                $"Add-on '{folderName}' not found on mod '{parentMod.GetDisplayName()}'");

        var (root, dir) = location.Value;
        var targetName = enable
            ? ModFolderHelpers.GetFolderNameWithoutDisabledPrefix(dir.Name)
            : ModFolderHelpers.GetFolderNameWithDisabledPrefix(dir.Name);
        if (!dir.Name.Equals(targetName, StringComparison.Ordinal))
            dir.MoveTo(Path.Combine(root.FullName, targetName));

        await SaveAddonsAsync(parentMod).ConfigureAwait(false);
    }

    private static (DirectoryInfo Root, DirectoryInfo Dir)? FindAddonDirectory(ISkinMod parentMod, string folderName)
    {
        var modRoot = new DirectoryInfo(parentMod.FullPath);

        static DirectoryInfo? Match(DirectoryInfo root, string folderName) =>
            root.EnumerateDirectories().FirstOrDefault(d =>
                d.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase) ||
                ModFolderHelpers.FolderNameEquals(d.Name, folderName));

        // Canonical layout: flat under the mod folder, beside the variant roots.
        var topLevel = Match(modRoot, folderName);
        if (topLevel is not null)
            return (modRoot, topLevel);

        // Fallback: one level deep (early nested builds and hand-built layouts).
        foreach (var root in modRoot.EnumerateDirectories())
        {
            var nested = Match(root, folderName);
            if (nested is not null)
                return (root, nested);
        }
        return null;
    }

    /// <summary>Re-mirrors the current on-disk state into .JASM_ModConfig.json.</summary>
    private static async Task SaveAddonsAsync(ISkinMod mod)
    {
        var addons = DetectAddons(mod);
        if (addons is null)
            return;

        try
        {
            var settings = await mod.Settings.ReadSettingsAsync().ConfigureAwait(false);
            var updated = addons.Select(a => new ModAddon(a.Name, a.FolderName, a.Enabled)).ToList();
            await mod.Settings.SaveSettingsAsync(settings.DeepCopyWithAddons(updated)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.Warning(e, "Could not save add-on state for {Mod}", mod.GetDisplayName());
        }
    }
}
