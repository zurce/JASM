using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.Services.Protocol;
using Serilog;

namespace JASM.Tests;

/// <summary>
/// Regression cover for the reported failure: a GameBanana link for ZZZ mod 722205 ("Claret Flint") could not be
/// resolved to a character even though the user had it — JASM's asset calls the character "Claret" and only the
/// asset's curated <c>Keys</c> aliases contain GameBanana's full name.
///
/// Runs against the real game assets, because the bug was in the *data* being consulted, not in string handling.
/// </summary>
public class OneClickCharacterResolutionTests : IDisposable
{
    private const string GameAssetsPath = @"..\..\..\..\GIMI-ModManager.WinUI\Assets\Games";

    private readonly string _tmpDataDirectory =
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    private readonly MockLogger _logger = new();

    private async Task<IGameService> InitGameServiceAsync(string game)
    {
        Log.Logger = _logger;

        var service = new GameService(_logger, new MockLocalizer());
        await service.InitializeAsync(new InitializationOptions
        {
            AssetsDirectory = Path.Combine(GameAssetsPath, game),
            LocalSettingsDirectory = _tmpDataDirectory,
            CharacterSkinsAsCharacters = false
        });

        return service;
    }

    [Fact]
    public async Task Resolves_ZzzCharacter_FromGameBananaFullName_ViaAssetAliases()
    {
        // GameBanana category: "Claret Flint" (mod 722205). JASM asset: InternalName/DisplayName "Claret",
        // Keys ["claret", "claret flint", "claretflint"].
        var zzz = await InitGameServiceAsync("ZZZ");

        var character = OneClickCharacterResolver.Resolve("Claret Flint", zzz.GetCharacters(includeDisabled: true));

        Assert.NotNull(character);
        Assert.Equal("claret", character!.InternalName.Id);
    }

    [Theory]
    // GameBanana uses the full in-game name; the asset uses the short one.
    [InlineData("Anby Demara", "anby")]
    [InlineData("Astra Yao", "astra")]
    [InlineData("Burnice White", "burnice")]
    [InlineData("Caesar King", "caesar")]
    [InlineData("Piper Wheel", "piper")]
    public async Task Resolves_ZzzCharacter_FromFullGameBananaName_ByWordPrefix(string category, string expected)
    {
        var zzz = await InitGameServiceAsync("ZZZ");

        var character = OneClickCharacterResolver.Resolve(category, zzz.GetCharacters(includeDisabled: true));

        Assert.NotNull(character);
        Assert.Equal(expected, character!.InternalName.Id);
    }

    [Fact]
    public async Task Resolves_Character_WhenTheCategoryUsesTheLocalizedFullName()
    {
        // ZZZ "Silver Soldier - Anby" is displayed as "Soldier 0 - Anby" (en) / "Soldier 0 Anby" on
        // GameBanana - the asset's internal name shares no prefix with it, so this only resolves through the
        // display name / word matching.
        var zzz = await InitGameServiceAsync("ZZZ");

        var character = OneClickCharacterResolver.Resolve("Soldier 0 Anby", zzz.GetCharacters(includeDisabled: true));

        Assert.NotNull(character);
        Assert.Equal("silver soldier - anby", character!.InternalName.Id);
    }

    [Fact]
    public async Task Resolves_SurnameFirstCategory_ViaTheAssetAlias()
    {
        // GameBanana category "Hoshimi Miyabi" (surname first). JASM's asset calls her "Miyabi"; the asset's
        // Keys carry the surname, the same way Nekomata carries "Nekomiya" and Yanagi carries "Tsukishiro".
        var zzz = await InitGameServiceAsync("ZZZ");

        var character = OneClickCharacterResolver.Resolve("Hoshimi Miyabi", zzz.GetCharacters(includeDisabled: true));

        Assert.NotNull(character);
        Assert.Equal("miyabi", character!.InternalName.Id);
    }

    [Fact]
    public async Task DoesNotMatch_WhenTheNameOnlyAppearsInTheMiddle()
    {
        // A category that merely contains a character name must not match it: the installer would otherwise
        // silently target the wrong character. A notification asking the user is the correct outcome.
        var zzz = await InitGameServiceAsync("ZZZ");

        Assert.Null(OneClickCharacterResolver.Resolve("Totally Not Anby", zzz.GetCharacters(includeDisabled: true)));
    }

    [Fact]
    public async Task Resolves_ExactName_WithoutNeedingAliases()
    {
        var zzz = await InitGameServiceAsync("ZZZ");

        var character = OneClickCharacterResolver.Resolve("Miyabi", zzz.GetCharacters(includeDisabled: true));

        Assert.NotNull(character);
        Assert.Equal("miyabi", character!.InternalName.Id);
    }

    [Fact]
    public async Task Resolves_AlternateAlias_NotJustTheInternalName()
    {
        // ZZZ "Pryce" carries keys ["pryce", "roxy", "roxy ifrita pryce", "roxy pryce", "roxyifritapryce"].
        var zzz = await InitGameServiceAsync("ZZZ");

        var character = OneClickCharacterResolver.Resolve("Roxy Ifrita Pryce", zzz.GetCharacters(includeDisabled: true));

        Assert.NotNull(character);
        Assert.Equal("pryce", character!.InternalName.Id);
    }

    [Fact]
    public async Task Resolves_GenshinCharacter_WithSpacingDifferences()
    {
        // GameBanana: "Raiden Shogun"; JASM: same name, but the matcher must not care about spacing/punctuation.
        var genshin = await InitGameServiceAsync("Genshin");

        var character = OneClickCharacterResolver.Resolve("RaidenShogun", genshin.GetCharacters(includeDisabled: true));

        Assert.NotNull(character);
        Assert.Equal("raiden shogun", character!.InternalName.Id);
    }

    [Fact]
    public async Task UnknownCategory_ResolvesToNothing_RatherThanGuessing()
    {
        var zzz = await InitGameServiceAsync("ZZZ");

        Assert.Null(OneClickCharacterResolver.Resolve("Definitely Not A Character", zzz.GetCharacters(includeDisabled: true)));
        Assert.Null(OneClickCharacterResolver.Resolve("", zzz.GetCharacters(includeDisabled: true)));
        Assert.Null(OneClickCharacterResolver.Resolve(null, zzz.GetCharacters(includeDisabled: true)));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tmpDataDirectory))
                Directory.Delete(_tmpDataDirectory, true);
        }
        catch
        {
            // best effort
        }
    }
}