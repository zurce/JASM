using FluentValidation;
using GIMI_ModManager.Core.Contracts.Services;
using PathPicker = GIMI_ModManager.WinUI.ViewModels.SubVms.PathPicker;

namespace GIMI_ModManager.WinUI.Validators;

/// <summary>
/// Warns (or blocks) when the chosen mods folder is located inside/at the JASM
/// install directory. Placing mods under the install folder means a whole-folder
/// update swap can destroy them.
///   - Mods folder == JASM root        → Error severity (blocks save) — creating mods at
///                                       the very root is incompatible with auto-update.
///   - Mods folder inside JASM (not root) → Warning severity (allows save) — user's choice,
///                                       but must use the safe update path when updating.
/// </summary>
public class ModsFolderInsideJasm : AbstractValidator<PathPicker>
{
    // The install root never changes; normalize once so repeated validations
    // (folder picker validates on every keystroke) stay allocation-cheap.
    private static readonly string? JasmRoot = Normalize(App.ROOT_DIR);

    public ModsFolderInsideJasm()
    {
        RuleFor(x => x.Path)
            .Must(path => !IsJasmRoot(path))
            .WithMessage(_ => Localized("Settings_ModsFolder_IsJasmRoot",
                "The mods folder cannot be the JASM install folder root. Choose a different folder."))
            .WithSeverity(Severity.Error)
            .Must(path => !IsInsideJasm(path))
            .WithMessage(_ => Localized("Settings_ModsFolder_InsideJasmWarning",
                "This mods folder is inside the JASM install folder. It is recommended to place mods outside of JASM so they are not affected by updates."))
            .WithSeverity(Severity.Warning);
    }

    private static string Localized(string key, string fallback)
    {
        try
        {
            return App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault(key) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static bool IsJasmRoot(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               string.Equals(Normalize(path), JasmRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInsideJasm(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               JasmRoot is not null &&
               Normalize(path)!.StartsWith(JasmRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
