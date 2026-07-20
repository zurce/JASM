using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.WinUI.Services.AppManagement;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GIMI_ModManager.WinUI.ViewModels.SettingsViewModels;

internal class CharacterSkinsDialog
{
    private readonly IWindowManagerService _windowManagerService = App.GetService<IWindowManagerService>();
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public async Task<ContentDialogResult> ShowDialogAsync(bool isEnabled)
    {
        var dialog = new ContentDialog()
        {
            Title = isEnabled
                ? (_localizer.GetLocalizedStringOrDefault("Settings_CharacterSkins_DisableTitle") ?? "Disable Character Skins as Characters?")
                : (_localizer.GetLocalizedStringOrDefault("Settings_CharacterSkins_EnableTitle") ?? "Enable Character Skins as Characters?"),
            Content = new TextBlock()
            {
                Text = isEnabled
                    ? (_localizer.GetLocalizedStringOrDefault("Settings_CharacterSkins_DisableContent") ?? DisableContent)
                    : (_localizer.GetLocalizedStringOrDefault("Settings_CharacterSkins_EnableContent") ?? EnableContent),
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true
            },
            DefaultButton = ContentDialogButton.Primary,
            PrimaryButtonText = isEnabled
                ? (_localizer.GetLocalizedStringOrDefault("Settings_CharacterSkins_DisablePrimaryButton") ?? "Disable")
                : (_localizer.GetLocalizedStringOrDefault("Settings_CharacterSkins_EnablePrimaryButton") ?? "Enable"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Settings_CharacterSkins_CloseButton") ?? "Cancel"
        };


        return await _windowManagerService.ShowDialogAsync(dialog).ConfigureAwait(false);
    }

    private const string EnableContent =
        "Enabling this will make JASM treat in game skins as separate characters in the character overview.\n" +
        "This could potentially become the default setting of JASM in the future.\n" +
        "JASM will not move any of your mods nor will it delete any.\n\n" +
        "Are you sure you want to enable character skins as characters? JASM will restart afterwards...";

    private const string DisableContent =
        "Disabling this will make JASM treat in game skins as skins of the base character in the character overview.\n" +
        "This is currently the default setting of JASM\n" +
        "JASM will not move any of your mods nor will it delete any.\n\n" +
        "Are you sure you want to disable character skins as characters? JASM will restart afterwards...";
}