using FluentValidation;
using GIMI_ModManager.WinUI.ViewModels.SubVms;

namespace GIMI_ModManager.WinUI.Validators.PreConfigured;

public static class GimiFolderRootValidators
{
    public static ICollection<AbstractValidator<PathPicker>> Validators(IEnumerable<string> validMiExeFilenames, string? customMessage = null)
    {
        return new AbstractValidator<PathPicker>[]
        {
            new IsValidPathFormat(),
            new FolderExists(),
            new ContainsAnyFileSystemEntryWithNames(validMiExeFilenames, customMessage: customMessage, warning: true)
        };
    }
}