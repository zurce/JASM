using GIMI_ModManager.Core.Contracts.Entities;
using GIMI_ModManager.Core.Entities.Mods.Contract;
using GIMI_ModManager.Core.Helpers;
using Serilog;

namespace GIMI_ModManager.Core.Services;

/// <summary>
/// Variant support for mods installed with multiple GameBanana files ("variants").
/// Layout inside a variant-aware mod folder — each variant is a plain-named folder; the active
/// variant is the one without the DISABLED_ prefix:
/// <code>
/// coolmod/
///   .JASM_ModConfig.json      (variants array — presence marks the mod as variant-aware)
///   ramielle_mod/...          (active variant)
///   DISABLED_pink/...         (inactive variant)
/// </code>
/// Only one variant may be active at a time; enabling one disables all others.
/// The disk (DISABLED_ prefix) is the source of truth for enabled state; the config mirrors it
/// and defines WHICH folders are variants.
/// </summary>
public static class VariantManager
{
    private static readonly ILogger Logger = Log.ForContext(typeof(VariantManager));

    /// <summary>
    /// Detects the variants of a mod. A mod supports variants when its .JASM_ModConfig.json has a
    /// non-empty variants array; the folders named there are matched on disk.
    /// Returns null when the mod does not support variants.
    /// </summary>
    public static IReadOnlyList<ModVariant>? DetectVariants(ISkinMod mod)
    {
        if (!mod.Settings.TryGetSettings(out var settings) || settings.Variants is not { Count: > 0 })
            return null;

        var root = new DirectoryInfo(mod.FullPath);

        var result = new List<ModVariant>();
        foreach (var configEntry in settings.Variants)
        {
            // Match by stored folder name, or by the unprefixed form of it (state may have changed).
            var folder = root.EnumerateDirectories()
                .FirstOrDefault(d =>
                    d.Name.Equals(configEntry.FolderName, StringComparison.OrdinalIgnoreCase) ||
                    ModFolderHelpers.FolderNameEquals(d.Name, configEntry.FolderName) ||
                    ModFolderHelpers.FolderNameEquals(d.Name, configEntry.Name));

            if (folder is null)
            {
                Logger.Warning("Variant folder '{Folder}' of mod '{Mod}' is missing on disk",
                    configEntry.FolderName, mod.GetDisplayName());
                continue;
            }

            var enabled = !ModFolderHelpers.FolderHasDisabledPrefix(folder.Name);

            result.Add(new ModVariant(
                Name: string.IsNullOrWhiteSpace(configEntry.Name)
                    ? ModFolderHelpers.GetFolderNameWithoutDisabledPrefix(folder.Name)
                    : configEntry.Name,
                FolderName: folder.Name,
                Enabled: enabled));
        }

        return result.Count == 0
            ? null
            : result.OrderByDescending(v => v.Enabled)
                .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    /// <summary>
    /// Activates the given variant and disables all other variants. Exactly one variant is active
    /// at any time. The whole mod folder must be enabled (no global DISABLED_ prefix) — callers are
    /// responsible for enabling the mod itself first.
    /// </summary>
    public static async Task SetActiveVariantAsync(ISkinMod mod, string variantName)
    {
        var variants = DetectVariants(mod);
        if (variants is null || variants.Count == 0)
            throw new InvalidOperationException($"Mod '{mod.GetDisplayName()}' has no variants");

        var target = variants.FirstOrDefault(v => v.Name.Equals(variantName, StringComparison.OrdinalIgnoreCase));
        if (target is null)
            throw new InvalidOperationException($"Variant '{variantName}' not found on mod '{mod.GetDisplayName()}'");

        if (target.Enabled && variants.Count(v => v.Enabled) == 1)
            return; // already the single active variant

        var root = new DirectoryInfo(mod.FullPath);

        foreach (var variant in variants.Where(v => v.Enabled && !v.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase)))
        {
            DisableVariant(root, variant);
        }

        EnableVariant(root, target);

        await SaveVariantsAsync(mod).ConfigureAwait(false);

        Logger.Information("Activated variant '{Variant}' on mod '{Mod}'", target.Name, mod.GetDisplayName());
    }

    /// <summary>Renames a variant (its folder on disk), preserving its enabled state.</summary>
    public static async Task RenameVariantAsync(ISkinMod mod, string currentName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new InvalidOperationException("Variant name cannot be empty");

        newName = newName.Trim();

        var variants = DetectVariants(mod);
        if (variants is null)
            throw new InvalidOperationException($"Mod '{mod.GetDisplayName()}' has no variants");

        var target = variants.FirstOrDefault(v => v.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase));
        if (target is null)
            throw new InvalidOperationException($"Variant '{currentName}' not found on mod '{mod.GetDisplayName()}'");

        if (newName.Equals(currentName, StringComparison.OrdinalIgnoreCase))
            return;

        if (variants.Any(v => !v.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase) &&
                              v.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A variant named '{newName}' already exists");

        var root = new DirectoryInfo(mod.FullPath);
        var dir = new DirectoryInfo(Path.Combine(root.FullName, target.FolderName));
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Variant folder not found: {dir.FullName}");

        var enabled = !ModFolderHelpers.FolderHasDisabledPrefix(dir.Name);
        var renamedFolderName = enabled ? newName : VariantFolderHelpers.GetDisabledVariantFolderName(newName);

        dir.MoveTo(Path.Combine(root.FullName, renamedFolderName));

        // Update the renamed entry directly — re-detecting here would fail to match the renamed
        // folder against the stale config FolderNames and silently drop the variant.
        var updated = variants
            .Select(v => v.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase)
                ? v with { Name = newName, FolderName = renamedFolderName }
                : v)
            .ToList();

        try
        {
            var settings = await mod.Settings.ReadSettingsAsync().ConfigureAwait(false);
            await mod.Settings.SaveSettingsAsync(settings.DeepCopyWithVariants(updated)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.Warning(e, "Could not save variant state for {Mod}", mod.GetDisplayName());
        }

        Logger.Information("Renamed variant '{Old}' to '{New}' on mod '{Mod}'", currentName, newName,
            mod.GetDisplayName());
    }

    private static void DisableVariant(DirectoryInfo root, ModVariant variant)
    {
        var path = Path.Combine(root.FullName, variant.FolderName);
        if (!Directory.Exists(path))
            return;

        var dir = new DirectoryInfo(path);
        var disabledName = VariantFolderHelpers.GetDisabledVariantFolderName(dir.Name);
        if (!dir.Name.Equals(disabledName, StringComparison.OrdinalIgnoreCase))
            dir.MoveTo(Path.Combine(root.FullName, disabledName));
    }

    private static void EnableVariant(DirectoryInfo root, ModVariant variant)
    {
        var path = Path.Combine(root.FullName, variant.FolderName);
        if (!Directory.Exists(path))
            return;

        var dir = new DirectoryInfo(path);
        var enabledName = VariantFolderHelpers.GetEnabledVariantFolderName(dir.Name);
        if (!dir.Name.Equals(enabledName, StringComparison.OrdinalIgnoreCase))
            dir.MoveTo(Path.Combine(root.FullName, enabledName));
    }

    /// <summary>Re-mirrors the current on-disk state into .JASM_ModConfig.json.</summary>
    private static async Task SaveVariantsAsync(ISkinMod mod)
    {
        var variants = DetectVariants(mod);
        if (variants is null)
            return;

        try
        {
            var settings = await mod.Settings.ReadSettingsAsync().ConfigureAwait(false);
            await mod.Settings.SaveSettingsAsync(settings.DeepCopyWithVariants(variants)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.Warning(e, "Could not save variant state for {Mod}", mod.GetDisplayName());
        }
    }

    /// <summary>Builds the variants config array for a freshly installed variant mod:
    /// the first archive enabled, all extras disabled. Names are the bare archive base names.</summary>
    public static IReadOnlyList<ModVariant> CreateInitialVariants(IReadOnlyCollection<string> variantNames,
        string mainVariantName)
    {
        return variantNames
            .Select(name => new ModVariant(
                Name: name,
                FolderName: name.Equals(mainVariantName, StringComparison.OrdinalIgnoreCase)
                    ? name
                    : VariantFolderHelpers.GetDisabledVariantFolderName(name),
                Enabled: name.Equals(mainVariantName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}