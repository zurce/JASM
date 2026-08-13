using System.Text.Json;
using GIMI_ModManager.Core.Services.GameBanana.ApiModels;

namespace JASM.Tests;

public class ApiSearchDeserializationTests
{
    // Captured shape of the GameBanana "Util/Search/Results" API response.
    private const string SearchJson = @"{
  ""_aMetadata"": { ""_nRecordCount"": 1, ""_aSectionMatchCounts"": [ { ""_sModelName"": ""Mod"", ""_nMatchCount"": 1 } ] },
  ""_aRecords"": [
    {
      ""_idRow"": 672817,
      ""_sModelName"": ""Mod"",
      ""_sName"": ""Velina lauma"",
      ""_sProfileUrl"": ""https:\/\/gamebanana.com\/mods\/672817"",
      ""_tsDateAdded"": 1777296520
    }
  ]
}";

    [Fact]
    public void Deserializes_SearchResponse()
    {
        var root = JsonSerializer.Deserialize<ApiSearchResponse>(SearchJson);
        Assert.NotNull(root);
        Assert.NotNull(root.Records);
        var first = Assert.Single(root.Records);
        Assert.Equal(672817, first.ModId);
        Assert.Equal("Velina lauma", first.Name);
        Assert.Equal("https://gamebanana.com/mods/672817", first.ProfileUrl);
    }

    [Fact]
    public void EmptyResponse_HasNoRecords_DoesNotThrow()
    {
        var root = JsonSerializer.Deserialize<ApiSearchResponse>("{ \"_aRecords\": [] }");
        Assert.NotNull(root);
        Assert.Empty(root.Records!);
    }
}