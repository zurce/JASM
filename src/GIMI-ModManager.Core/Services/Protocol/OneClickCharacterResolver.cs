using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.GamesService.Interfaces;

namespace GIMI_ModManager.Core.Services.Protocol;

/// <summary>
/// Resolves the GameBanana category of a submission to one of JASM's characters.
///
/// For the games JASM+ manages, a GameBanana category is the character (e.g. "Klee", "Claret Flint") or a
/// section ("Others", "Weapons"). The names do not always agree: GameBanana uses the full in-game name while
/// JASM's assets may use a short one, which is why the asset's curated <see cref="ICharacter.Keys"/> aliases are
/// consulted (see <see cref="CharacterNameMatcher.NamesFor"/>).
///
/// Deliberately conservative: it matches names, never guesses, and returns null when nothing matches so the
/// caller can tell the user instead of installing into the wrong character's folder.
/// </summary>
public static class OneClickCharacterResolver
{
    public static ICharacter? Resolve(string? gameBananaCategoryName, IEnumerable<ICharacter> characters)
    {
        if (string.IsNullOrWhiteSpace(gameBananaCategoryName))
            return null;

        var scored = characters
            .Select(character => (Character: character, Score: CharacterNameMatcher.Score(
                gameBananaCategoryName,
                CharacterNameMatcher.NamesFor(character.InternalName, character.DisplayName,
                    character.ModFilesName, character.Keys))))
            .Where(candidate => candidate.Score > 0)
            .ToList();

        if (scored.Count == 0)
            return null;

        var best = scored.Max(candidate => candidate.Score);
        var winners = scored.Where(candidate => candidate.Score == best).Select(candidate => candidate.Character).ToList();

        // Two characters match equally well ("Anby" and a hypothetical "Demara" for "Anby Demara"): refuse
        // rather than guess, so the user picks the target in the installer.
        return winners.Count == 1 ? winners[0] : null;
    }
}