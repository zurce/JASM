# JASM-ENDFIELD: Initial Support for Arknights: Endfield

**Tracker**: `JASM-ENDFIELD`
**Epic**: Custom JASM Features & Release Support
**Status**: Completed (Merged)
**Branch**: `zurce/add-endfield-support`

---

## Summary
Adds initial support and profile configuration for **Arknights: Endfield** as a selectable game in JASM. This includes setting up character JSON mappings, elements, NPCs, weapons, and icon assets.

---

## Related Tasks
- Depends on: `JASM-COMMUNITY-GAMES` (for remote Git loading support of new community game databases).

---

## What Was Built

### New Files
- Image resources for Endfield characters (e.g. `Akekuri.webp`, `Alesh.webp`, `Pog.webp`) located in `src/GIMI-ModManager.Core/Assets/Games/Endfield/Images/Characters/`.
- Element images (`Cryo.webp`, `Electric.webp`, `Heat.webp`, `Nature.webp`, `Physical.webp`) in `src/GIMI-ModManager.Core/Assets/Games/Endfield/Images/Elements/`.
- `characters.json`, `elements.json`, `game.json`, `npcs.json`, `objects.json`, `regions.json`, `weaponClasses.json`, `weapons.json` containing profile settings for Endfield.

### Modified Files
- `FileNames.cs` — Added Endfield game identification config.
- `SelectedGameService.cs` — Registered Endfield as a supported game target.
- `SettingsViewModel.cs` — Updated profiles list UI binding.
- `SettingsPage.xaml` — Enabled layout sizing logic for Endfield.

---

## Key Technical Details
- Registered the Special Folder structures and directories for Endfield so that the app's startup wizard detects Endfield MI executable and mods path correctly.
- Added Endfield characters database with elements, weapon types, and rarity values mapping.
