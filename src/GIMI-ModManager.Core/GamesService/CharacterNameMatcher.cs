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

    /// <summary>True when the GameBanana category name matches any of the candidate (JASM) names.</summary>
    public static bool Matches(string? gameBananaCategoryName, IEnumerable<string?> candidateNames) =>
        Score(gameBananaCategoryName, candidateNames) > 0;

    /// <summary>
    /// How well a GameBanana category name matches one character's names. Higher is better, 0 means no match.
    ///
    /// GameBanana uses the full in-game name while JASM's assets may use a short one ("Anby Demara" vs "Anby",
    /// "Astra Yao" vs "Astra"), so a candidate that is a *word* prefix of the category counts — word-based, not
    /// character-based, so "Klee" never matches "Klee2".
    ///
    /// Deliberately NOT symmetric: "Soldier 0 Anby" does not match "Anby" (in ZZZ that is a different agent),
    /// and a category that merely contains a name in the middle does not match either. A miss tells the user to
    /// pick the character; a wrong match installs into the wrong folder.
    /// </summary>
    public static int Score(string? gameBananaCategoryName, IEnumerable<string?> candidateNames)
    {
        var wanted = Words(gameBananaCategoryName);
        if (wanted.Count == 0)
            return 0;

        var wantedNormalized = Normalize(gameBananaCategoryName);

        var best = 0;
        foreach (var candidateName in candidateNames)
        {
            var candidate = Words(candidateName);
            if (candidate.Count == 0)
                continue;

            int score;
            if (Normalize(candidateName) == wantedNormalized)
            {
                // Same name, whatever the spacing/punctuation: "RaidenShogun" == "Raiden Shogun".
                score = 200 + wanted.Count;
            }
            else if (candidate.Count <= wanted.Count && wanted.Take(candidate.Count).SequenceEqual(candidate))
            {
                // GameBanana's full name starts with the asset's short one: "Anby Demara" ~ "Anby".
                score = 100 + candidate.Count;
            }
            else
            {
                continue;
            }

            if (score > best)
                best = score;
        }

        return best;
    }

    /// <summary>Splits a name into lowercase alphanumeric words: "Raiden Shogun" → [raiden, shogun].</summary>
    public static IReadOnlyList<string> Words(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var words = new List<string>();
        var current = new List<char>();

        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Add(char.ToLowerInvariant(c));
                continue;
            }

            if (current.Count > 0)
            {
                words.Add(new string(current.ToArray()));
                current.Clear();
            }
        }

        if (current.Count > 0)
            words.Add(new string(current.ToArray()));

        return words;
    }

    /// <summary>
    /// Every name that identifies a character, in matching order: the asset's <paramref name="keys"/> are the
    /// curated alias list (e.g. ZZZ's "Claret" carries "claret flint", which is what GameBanana calls the
    /// category), then the internal/display/mod-folder names.
    ///
    /// Without the keys, a category like GameBanana's "Claret Flint" never matches JASM's "Claret" — that was a
    /// real failure reported by the user, which is why this is explicit rather than "just compare three names".
    /// </summary>
    public static IEnumerable<string?> NamesFor(string? internalName, string? displayName, string? modFilesName,
        IEnumerable<string>? keys)
    {
        if (keys is not null)
        {
            foreach (var key in keys)
                yield return key;
        }

        yield return internalName;
        yield return displayName;
        yield return modFilesName;
    }
}