using System.Text.Json;
using GIMI_ModManager.Core.Services.GameBanana.ApiModels;
using GIMI_ModManager.Core.Services.GameBanana.Models;

namespace JASM.Tests;

/// <summary>
/// 1-click installs pick the target game and character from the submission's profile payload, so the mapping of
/// <c>_aGame</c> / <c>_aCategory</c> into JASM's model is load-bearing. Payload captured from
/// <c>get apiv11/Mod/691863/ProfilePage</c> (the live mod the user linked while researching this feature).
/// </summary>
public class ModPageInfoMappingTests
{
    private const string ProfileJson = """
    {
      "_idRow": 691863,
      "_sName": "Yamato remodel",
      "_sProfileUrl": "https://gamebanana.com/mods/691863",
      "_aGame": { "_idRow": 20948, "_sName": "Deadlock" },
      "_aCategory": { "_idRow": 33328, "_sName": "Yamato" },
      "_aSubmitter": { "_sName": "Someone", "_sProfileUrl": "https://gamebanana.com/members/1" },
      "_aFiles": [
        { "_idRow": 1831455, "_sFile": "yamato.zip", "_sDownloadUrl": "https://gamebanana.com/dl/1831455" }
      ]
    }
    """;

    [Fact]
    public void Maps_GameAndCategoryIntoJasmModel()
    {
        var apiProfile = JsonSerializer.Deserialize<ApiModProfile>(ProfileJson);
        Assert.NotNull(apiProfile);

        var modPageInfo = new ModPageInfo(apiProfile!);

        Assert.Equal(20948, modPageInfo.GameBananaGameId);
        Assert.Equal("Deadlock", modPageInfo.GameBananaGameName);
        Assert.Equal("Yamato", modPageInfo.GameBananaCategoryName);
        Assert.Equal("Yamato remodel", modPageInfo.ModName);
    }

    [Fact]
    public void MissingGameAndCategory_DoesNotThrow()
    {
        var apiProfile = JsonSerializer.Deserialize<ApiModProfile>("""{ "_idRow": 1, "_sName": "x" }""");

        var modPageInfo = new ModPageInfo(apiProfile!);

        Assert.Equal(-1, modPageInfo.GameBananaGameId);
        Assert.Null(modPageInfo.GameBananaGameName);
        Assert.Null(modPageInfo.GameBananaCategoryName);
    }

    [Fact]
    public void Parses_RealGenshinCategoryName()
    {
        // Payload shape for a Genshin mod: the category is the character, which is what the resolver matches.
        var apiProfile = JsonSerializer.Deserialize<ApiModProfile>("""
        {
          "_idRow": 534833,
          "_sName": "Soap's Klee",
          "_aGame": { "_idRow": 8552, "_sName": "Genshin Impact" },
          "_aCategory": { "_idRow": 1, "_sName": "Klee" }
        }
        """);

        var modPageInfo = new ModPageInfo(apiProfile!);

        Assert.Equal(8552, modPageInfo.GameBananaGameId);
        Assert.Equal("Klee", modPageInfo.GameBananaCategoryName);
    }
}