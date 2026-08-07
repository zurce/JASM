using FluentValidation;
using GIMI_ModManager.WinUI.ViewModels.SubVms;

namespace GIMI_ModManager.WinUI.Validators.PreConfigured;

public static class GimiFolderRootValidators
{
    public static ICollection<AbstractValidator<PathPicker>> Validators(IEnumerable<string> validMiExeFilenames,
        string? customMessage = null, string? expectedXxmiIdentifier = null)
    {
        var validators = new List<AbstractValidator<PathPicker>>
        {
            new IsValidPathFormat(),
            new FolderExists(),
            new ContainsExpectedMiEntryOrXxmi(validMiExeFilenames, customMessage: customMessage)
        };

        // When we know the expected XXMI game, hard-block picking a different game's XXMI folder.
        if (!string.IsNullOrEmpty(expectedXxmiIdentifier))
            validators.Add(new WrongGameXxmiFolderError(expectedXxmiIdentifier));

        return validators;
    }
}