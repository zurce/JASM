# JASM-CHAR-COMMANDS: Custom Commands in Character Details Context Menu

**Tracker**: `JASM-CHAR-COMMANDS`
**Epic**: Custom JASM Features & Release Support
**Status**: Completed (Merged)
**Branch**: `feature/character-details-commands`

---

## Summary
Added support for showing and executing custom defined command-line actions directly from the Character Details page context menu. Previously, commands could only be launched from the general Settings page.

---

## Related Tasks
- Feeds into: `JASM-BATCH-CONFIG` (visual layout reference for batch menus).

---

## What Was Built

### Modified Files
- `ContextMenuVM.cs` — Populated context menu items with custom command actions that contain `{{TargetPath}}` variables, passing character details as parameters.
- `CharacterDetailsPage.xaml` — Updated context menu flyout to render custom commands items dynamically.

---

## Key Technical Details
- Commands are filtered to ensure they contain custom variables (such as `{{TargetPath}}`) before being added to the Character Details context menu flyout.
- When clicked, the command is executed with the target path set to the active character's mod directory.
