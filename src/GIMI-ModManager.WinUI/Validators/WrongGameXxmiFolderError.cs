using FluentValidation;
using GIMI_ModManager.WinUI.ViewModels.SubVms;

namespace GIMI_ModManager.WinUI.Validators;

/// <summary>
/// Hard-errors when the selected 3DMigoto root is an XXMI folder for a <em>different</em> game
/// than the one currently being configured (e.g. pointing at the GIMI folder while configuring
/// Star Rail / SRMI). This prevents pairing the wrong game's XXMI installation.
/// Does nothing when the folder is not XXMI or is the correct game's XXMI folder.
/// </summary>
public class WrongGameXxmiFolderError : AbstractValidator<PathPicker>
{
    public WrongGameXxmiFolderError(string? expectedXxmiIdentifier)
    {
        RuleFor(x => x.Path)
            .Must(path => !IsWrongGameXxmi(path))
            .WithMessage(_ => BuildMessage())
            .WithSeverity(Severity.Error);

        string BuildMessage()
        {
            // Generic "wrong game" message plus the expected XXMI identifier (e.g. SRMI).
            return $"This is the wrong XXMI folder for this game. Expected XXMI identifier: {expectedXxmiIdentifier}.";
        }

        bool IsWrongGameXxmi(string? path)
        {
            if (string.IsNullOrEmpty(expectedXxmiIdentifier) || string.IsNullOrWhiteSpace(path))
                return false;
            var detected = GIMI_ModManager.Core.Services.XxmiInstallationDetector.TryDetect(path);
            return detected is not null &&
                   !string.Equals(detected.GameIdentifier, expectedXxmiIdentifier, StringComparison.OrdinalIgnoreCase);
        }
    }
}
