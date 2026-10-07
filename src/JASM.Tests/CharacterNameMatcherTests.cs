using GIMI_ModManager.Core.GamesService;

namespace JASM.Tests;

/// <summary>
/// GameBanana category names and JASM character names disagree on punctuation/spacing ("Raiden Shogun" vs
/// "RaidenShogun"), so 1-click installs match on letters and digits only. A wrong match installs into the
/// wrong character's folder, which is why this is pinned by tests.
/// </summary>
public class CharacterNameMatcherTests
{
    [Theory]
    [InlineData("Raiden Shogun", "Raiden Shogun", true)]
    [InlineData("Raiden Shogun", "raiden shogun", true)]
    [InlineData("Raiden Shogun", "RaidenShogun", true)]
    [InlineData("Raiden Shogun", "Raiden_Shogun", true)]
    [InlineData("Klee", "Klee", true)]
    [InlineData("Klee", "klee", true)]
    [InlineData("Klee", "Klee (Palette)", false)]
    [InlineData("Klee", "Klee2", false)]
    [InlineData("Others", "Others", true)]
    [InlineData("Others", "Other", false)]
    [InlineData("", "Klee", false)]
    [InlineData("   ", "Klee", false)]
    [InlineData(null, "Klee", false)]
    [InlineData("Klee", "", false)]
    public void Matches_ComparesOnLettersAndDigitsOnly(string? categoryName, string candidate, bool expected)
        => Assert.Equal(expected, CharacterNameMatcher.Matches(categoryName, [candidate]));

    [Fact]
    public void Matches_AnyOfTheCandidateNames()
    {
        // A character exposes several names: internal name, display name and the on-disk mod folder name.
        Assert.True(CharacterNameMatcher.Matches("Klee", ["klee_internal", "Klee"]));
        Assert.False(CharacterNameMatcher.Matches("Klee", ["klee_internal", "Klee2"]));
    }

    [Theory]
    [InlineData("Raiden Shogun", "raidenshogun")]
    [InlineData("Klee", "klee")]
    [InlineData("Klee's Outfit!", "kleesoutfit")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_KeepsLettersAndDigits(string? input, string expected)
        => Assert.Equal(expected, CharacterNameMatcher.Normalize(input));
}