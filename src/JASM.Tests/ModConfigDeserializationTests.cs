using GIMI_ModManager.Core.Entities.Mods.Contract;
using GIMI_ModManager.Core.Helpers;
using System.Text.Json;
using GIMI_ModManager.Core.Entities.Mods.FileModels;

namespace JASM.Tests;

public class ModConfigDeserializationTests
{
    // Mirrors the user's "before" payload from the bug report.
    private const string CamelCaseJson = @"
{
  ""id"": ""98369323-ba28-4da4-a69e-5c368dbe4fb6"",
  ""customName"": ""Velina Stardust Makeup"",
  ""author"": ""MauxRose"",
  ""modUrl"": ""https://gamebanana.com/mods/692258"",
  ""imagePath"": "".JASM_Cover.jpg"",
  ""characterSkinOverride"": ""default_velinaairgid"",
  ""description"": """",
  ""dateAdded"": ""09.07.2026 11:42:05"",
  ""lastChecked"": ""13.08.2026 00:02:00""
}";

    [Fact]
    public void DefaultOptions_Drop_CamelCaseFields()
    {
        var opts = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var s = JsonSerializer.Deserialize<JsonModSettings>(CamelCaseJson, opts);
        Assert.NotNull(s);
        // Default (case-sensitive) resolver: camelCase keys do NOT map to PascalCase props.
        Assert.Null(s.CustomName);
        Assert.Null(s.Author);
        Assert.Null(s.ModUrl);
        Assert.Null(s.CharacterSkinOverride);
    }

    [Fact]
    public void CaseInsensitive_Preserves_CamelCaseFields()
    {
        var opts = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, PropertyNameCaseInsensitive = true };
        var s = JsonSerializer.Deserialize<JsonModSettings>(CamelCaseJson, opts);
        Assert.NotNull(s);
        Assert.Equal("Velina Stardust Makeup", s.CustomName);
        Assert.Equal("MauxRose", s.Author);
        Assert.Equal("https://gamebanana.com/mods/692258", s.ModUrl);
        Assert.Equal("default_velinaairgid", s.CharacterSkinOverride);
    }

    [Fact]
    public void ContainmentArrays_Survive_JsonRoundTrip()
    {
        var original = new JsonModSettings
        {
            Id = Guid.NewGuid().ToString(),
            Variants =
            [
                new JsonVariantEntry { Name = "main", FolderName = "main", Enabled = true }
            ],
            Addons =
            [
                new JsonAddonEntry { Name = "addonA", FolderName = "addonA", Enabled = true }
            ]
        };
        var json = JsonSerializer.Serialize(original);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var restored = JsonSerializer.Deserialize<JsonModSettings>(json, opts);
        Assert.NotNull(restored);
        Assert.Single(restored.Variants!);
        Assert.Single(restored.Addons!);
        Assert.Equal("addonA", restored.Addons![0].Name);
    }

    [Fact]
    public void DeepCopies_Preserve_VariantsAndAddons()
    {
        var settings = new ModSettings(Guid.NewGuid())
        {
            Variants = [new ModVariant("main", "main", true)],
            Addons = [new ModAddon("addonA", "addonA", true)]
        };

        // The installer re-save path (fresh settings + preserve copies) must drop neither array.
        var withProps = settings.DeepCopyWithProperties(customName: NewValue<string?>.Set("renamed"));
        Assert.NotNull(withProps.Variants);
        Assert.NotNull(withProps.Addons);

        var withVariants = settings.DeepCopyWithVariants(withProps.Variants!);
        Assert.NotNull(withVariants.Addons);

        var withAddons = settings.DeepCopyWithAddons(withProps.Addons!);
        Assert.NotNull(withAddons.Variants);
    }
}