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
}