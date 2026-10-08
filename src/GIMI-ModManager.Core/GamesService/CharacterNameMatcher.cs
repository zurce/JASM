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
    /// GameBanana and the game assets do not agree on naming, and the assets are community-maintained — they
    /// change without notice — so the resolution has to be tolerant in code rather than patched in data:
    ///
    ///   exact  - same name ignoring case/spacing/punctuation ("RaidenShogun" == "Raiden Shogun", and the
    ///            assets' curated <c>Keys</c> aliases land here: "Claret Flint" == key "claret flint").
    ///   prefix - GameBanana's full name starts with the asset's short one: "Anby Demara" ~ "Anby".
    ///   suffix - the surname-first form, and ONLY that shape: exactly two words against a one-word asset name,
    ///            e.g. "Hoshimi Miyabi" ~ "Miyabi" (assets may call her just "Miyabi").
    ///
    /// Word-based, so "Klee" never matches "Klee2". Deliberately asymmetric: "Soldier 0 Anby" does not match
    /// "Anby" (a different agent), and a name appearing in the middle never matches. A miss asks the user to
    /// pick; a wrong match installs into the wrong character's folder.
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
                score = 200 + wanted.Count;
            }
            else if (candidate.Count <= wanted.Count && wanted.Take(candidate.Count).SequenceEqual(candidate))
            {
                score = 100 + candidate.Count;
            }
            else if (wanted.Count == 2 && candidate.Count == 1 && wanted[1] == candidate[0])
            {
                // Surname-first only ("Hoshimi Miyabi"); anything longer is not assumed to be a name pattern.
                score = 50 + candidate.Count;
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