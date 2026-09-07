using GIMI_ModManager.Core.Contracts.Entities;
using GIMI_ModManager.Core.Entities.Mods.FileModels;
using GIMI_ModManager.Core.Entities.Mods.Helpers;
using GIMI_ModManager.Core.Helpers;

namespace GIMI_ModManager.Core.Entities.Mods.Contract;

public record ModSettings
{
    public ModSettings(Guid id, string? customName = null, string? author = null, string? version = null,
        Uri? modUrl = null, Uri? imagePath = null, string? characterSkinOverride = null, string? description = null,
        DateTime? dateAdded = null,
        DateTime? lastChecked = null, Uri? mergedIniPath = null, bool ignoreMergedIni = false,
        Dictionary<string, string>? preferences = null)
    {
        Id = id;
        CustomName = customName;
        Author = author;
        Version = version;
        ModUrl = modUrl;
        ImagePath = imagePath;
        CharacterSkinOverride = characterSkinOverride;
        Description = description;
        DateAdded = dateAdded;
        LastChecked = lastChecked;
        MergedIniPath = mergedIniPath;
        IgnoreMergedIni = ignoreMergedIni;
        _preferences = preferences;
    }

    public ModSettings DeepCopyWithProperties(NewValue<string?>? customName = null,
        NewValue<string?>? characterSkinOverride = null,
        NewValue<DateTime?>? newLastChecked = null, NewValue<Uri?>? mergedIniPath = null,
        NewValue<bool>? ignoreMergedIni = null,
        NewValue<string?>? author = null, NewValue<Uri?>? modUrl = null, NewValue<Uri?>? imagePath = null,
        NewValue<string?>? description = null
    )
    {
        return new ModSettings(
            Id,
            customName ?? CustomName,
            author ?? Author,
            Version,
            modUrl ?? ModUrl,
            imagePath ?? ImagePath,
            characterSkinOverride ?? CharacterSkinOverride,
            description ?? Description,
            DateAdded,
            newLastChecked ?? LastChecked,
            mergedIniPath ?? MergedIniPath,
            ignoreMergedIni ?? IgnoreMergedIni,
            _preferences is null ? null : new Dictionary<string, string>(_preferences))
        {
            // Copies must preserve the containment arrays: DeepCopyWithProperties callers
            // (skin override, settings update, merged-ini fixes, dupe rename) all re-save,
            // so dropping these would silently wipe them from .JASM_ModConfig.json.
            Variants = Variants,
            Addons = Addons
        };
    }

    internal ModSettings()
    {
    }

    public ModSettings DeepCopyWithVariants(IReadOnlyList<ModVariant> variants)
    {
        return new ModSettings(
            Id, CustomName, Author, Version, ModUrl, ImagePath, CharacterSkinOverride, Description,
            DateAdded, LastChecked, MergedIniPath, IgnoreMergedIni,
            _preferences is null ? null : new Dictionary<string, string>(_preferences))
        {
            Variants = variants,
            Addons = Addons
        };
    }

    public Guid Id { get; internal set; }

    public string? CustomName { get; internal set; }

    public string? Author { get; internal set; }

    public string? Version { get; internal set; }

    public Uri? ModUrl { get; internal set; }

    public Uri? ImagePath { get; internal set; }

    public Uri? MergedIniPath { get; internal set; }

    public bool IgnoreMergedIni { get; internal set; }

    public string? CharacterSkinOverride { get; internal set; }
    public string? Description { get; internal set; }

    public DateTime? DateAdded { get; internal set; }

    public DateTime? LastChecked { get; internal set; }

    private Dictionary<string, string>? _preferences;
    public IReadOnlyDictionary<string, string> Preferences => _preferences ??= new Dictionary<string, string>();

    public IReadOnlyList<ModVariant>? Variants { get; internal set; }

    public IReadOnlyList<ModAddon>? Addons { get; internal set; }


    internal static ModSettings FromJsonSkinSettings(ISkinMod? skinMod, JsonModSettings settings)
    {
        return new ModSettings
        {
            Id = SkinModHelpers.StringToGuid(settings.Id),
            CustomName = settings.CustomName,
            Author = settings.Author,
            Version = settings.Version,
            ModUrl = SkinModHelpers.StringUrlToUri(settings.ModUrl),
            ImagePath = skinMod is not null
                ? SkinModHelpers.RelativeModPathToAbsPath(skinMod.FullPath, settings.ImagePath)
                : null,
            CharacterSkinOverride = settings.CharacterSkinOverride,
            Description = settings.Description,
            DateAdded = DateTime.TryParse(settings.DateAdded, out var dateAdded) ? dateAdded : null,
            LastChecked = DateTime.TryParse(settings.LastChecked, out var lastChecked) ? lastChecked : null,
            MergedIniPath = skinMod is not null
                ? SkinModHelpers.RelativeModPathToAbsPath(skinMod.FullPath, settings.MergedIniPath)
                : null,
            IgnoreMergedIni = settings.MergedIniPath == string.Empty,
            _preferences = settings.Preferences is null
                ? null
                : new Dictionary<string, string>(settings.Preferences),
            Variants = settings.Variants is null
                ? null
                : settings.Variants.Select(v => new ModVariant(v.Name, v.FolderName, v.Enabled)).ToList(),
            Addons = settings.Addons is null
                ? null
                : settings.Addons.Select(a => new ModAddon(a.Name, a.FolderName, a.Enabled)).ToList()
        };
    }

    internal JsonModSettings ToJsonSkinSettings(ISkinMod skinMod)
    {
        return new JsonModSettings
        {
            Id = Id.ToString(),
            CustomName = CustomName,
            Author = Author,
            Version = Version,
            ModUrl = ModUrl?.ToString(),
            ImagePath = SkinModHelpers.UriPathToModRelativePath(skinMod, ImagePath?.LocalPath),
            CharacterSkinOverride = CharacterSkinOverride,
            Description = Description,
            DateAdded = DateAdded?.ToString(),
            LastChecked = LastChecked?.ToString(),
            MergedIniPath = IgnoreMergedIni
                ? ""
                : SkinModHelpers.UriPathToModRelativePath(skinMod, MergedIniPath?.LocalPath),
            Preferences = Preferences.Count == 0 ? null : new Dictionary<string, string>(Preferences),
            Variants = Variants is null || Variants.Count == 0
                ? null
                : Variants.Select(v => new JsonVariantEntry { Name = v.Name, FolderName = v.FolderName, Enabled = v.Enabled }).ToList(),
            Addons = Addons is null || Addons.Count == 0
                ? null
                : Addons.Select(a => new JsonAddonEntry { Name = a.Name, FolderName = a.FolderName, Enabled = a.Enabled }).ToList()
        };
    }


    public bool SettingsEquals(ModSettings? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (ReferenceEquals(other, null)) return false;

        if (Id != other.Id) return false;
        if (CustomName != other.CustomName) return false;
        if (Author != other.Author) return false;
        if (Version != other.Version) return false;
        if (ModUrl != other.ModUrl) return false;
        if (ImagePath != other.ImagePath) return false;
        if (CharacterSkinOverride != other.CharacterSkinOverride) return false;
        if (MergedIniPath != other.MergedIniPath) return false;
        if (IgnoreMergedIni != other.IgnoreMergedIni) return false;

        return true;
    }

    internal void SetPreferences(Dictionary<string, string>? preferences)
    {
        _preferences = preferences;
    }
}