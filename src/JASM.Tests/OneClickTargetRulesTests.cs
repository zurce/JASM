using GIMI_ModManager.Core.Services.Protocol;

namespace JASM.Tests;

/// <summary>
/// Pins when a 1-click install asks the user where to install. Getting this wrong either spams the user with a
/// dialog on every link, or silently installs into a guess — and the skin case is the one GameBanana cannot
/// express in the link at all.
/// </summary>
public class OneClickTargetRulesTests
{
    [Theory]
    // Setting off: always ask.
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, false, true)]
    // Setting on, character detected, no skins: nothing to decide -> install.
    [InlineData(true, true, false, false)]
    // Setting on, character detected, but it has skins: the skin is a real choice.
    [InlineData(true, true, true, true)]
    // Setting on, nothing detected: the user must pick (UI/weapon mods, new characters, ambiguity).
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, true)]
    public void RequiresConfirmation_OnlySkipsWhenThereIsNothingToDecide(bool installWithoutConfirmation,
        bool characterDetected, bool characterHasSkins, bool expected)
        => Assert.Equal(expected,
            OneClickTargetRules.RequiresConfirmation(installWithoutConfirmation, characterDetected, characterHasSkins));
}