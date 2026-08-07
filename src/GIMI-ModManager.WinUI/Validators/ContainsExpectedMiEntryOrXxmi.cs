using FluentValidation;
using GIMI_ModManager.Core.Services;
using GIMI_ModManager.WinUI.ViewModels.SubVms;

namespace GIMI_ModManager.WinUI.Validators;

/// <summary>
/// Validates a model-importer (3DMigoto) root folder. The folder passes if it either contains
/// any of the expected model-importer executable filenames, OR is recognized as an XXMI-managed
/// installation for the configured game. Emits a warning (not an error) when neither matches, so
/// an unrecognized folder is not hard-blocked (wrong-game pairings are caught by
/// <see cref="WrongGameXxmiFolderError"/> instead).
/// </summary>
public class ContainsExpectedMiEntryOrXxmi : AbstractValidator<PathPicker>
{
    public ContainsExpectedMiEntryOrXxmi(IEnumerable<string> validMiExeFilenames, string? customMessage = null)
    {
        var fileNamesArray = validMiExeFilenames is null ? Array.Empty<string>() : validMiExeFilenames.ToArray();
        var filenamesLowerArray = fileNamesArray.Select(name => name.ToLower()).ToArray();

        customMessage ??=
            $"Folder does not contain any entry with the specified names: {string.Join(" Or ", fileNamesArray)}" +
            " (or is not a recognized XXMI installation)";

        RuleFor(x => x.Path)
            .Must(path =>
                path is null ||
                Directory.Exists(path) == false ||
                ContainsExpectedEntry(path, filenamesLowerArray) ||
                // Recognized as *any* XXMI folder (correct or wrong game) is enough to suppress
                // this advisory warning. Wrong-game pairings are reported as a hard error by
                // WrongGameXxmiFolderError, so no redundant warning here.
                XxmiInstallationDetector.TryDetect(path) is not null
            )
            .WithMessage(customMessage)
            .WithSeverity(Severity.Warning);
    }

    private static bool ContainsExpectedEntry(string path, string[] filenamesLower)
    {
        try
        {
            return Directory.GetFileSystemEntries(path)
                .Any(entry => filenamesLower.Any(name => entry.ToLower().EndsWith(name)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}