namespace GIMI_ModManager.Core.GamesService;

/// <summary>
/// Matches GameBanana's category name against JASM character names.
///
/// For the games JASM+ manages, a GameBanana "category" is the character (e.g. <c>Klee</c>, <c>Raiden Shogun</c>)
/// or a section (<c>Others</c>, <c>Gliders</c>, <c>Weapons</c>). Names differ in punctuation and spacing between
/// the two sites, so comparison normalises to letters and digits only.
/// </summary>
public static class CharacterNameMatcher
{
    /// <summary>"Raiden Shogun", "raiden_shogun" and "RaidenShogun" all normalise to the same key.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var buffer = new char[value.Length];
        var length = 0;
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
                buffer[length++] = char.ToLowerInvariant(c);
        }

        return new string(buffer, 0, length);
    }

    /// <summary>True when the GameBanana category name equals any of the candidate (JASM) names.</summary>
    public static bool Matches(string? gameBananaCategoryName, IEnumerable<string?> candidateNames)
    {
        var wanted = Normalize(gameBananaCategoryName);
        return wanted.Length != 0 && candidateNames.Any(name => Normalize(name) == wanted);
    }
}