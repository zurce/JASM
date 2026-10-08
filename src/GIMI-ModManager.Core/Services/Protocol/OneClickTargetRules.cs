namespace GIMI_ModManager.Core.Services.Protocol;

/// <summary>
/// Decides whether a GameBanana 1-click install has to ask the user where the mod goes.
/// </summary>
public static class OneClickTargetRules
{
    /// <summary>
    /// "Install without asking for confirmation" only skips the prompt when there is genuinely nothing to decide:
    /// the character must have been detected *and* it must have no in-game skins. GameBanana cannot express a
    /// skin in a link (it has no concept of them), so a character with skins always leaves a choice to make — and
    /// an undetected character has to be picked (UI/weapon mods, new characters, ambiguous names).
    /// </summary>
    public static bool RequiresConfirmation(bool installWithoutConfirmation, bool characterDetected,
        bool characterHasSkins) =>
        !installWithoutConfirmation || !characterDetected || characterHasSkins;
}