# JASM-OVERRIDE: Force Overwrite and Folder Settings Override

**Tracker**: `JASM-OVERRIDE`
**Epic**: Custom JASM Features & Release Support
**Status**: Completed (Merged)
**Branch**: `zurce/add-override-folder`

---

## Summary
Implements support for setting override folders during mod installations. If a mod already exists under a different folder name, or the user wants to enforce overriding an existing folder, this feature prompts the user with force overwrite options and lets them specify custom folder names.

---

## Related Tasks
*None*

---

## What Was Built

### Modified Files
- `ModInstallerSettings.cs` — Added settings option for installer overrides.
- `ModInstallerService.cs` — Implemented folder deletion/overwrite logic during file extraction.
- `ModInstallerVM.cs` — Handled user selection logic for duplicate folders and directory overrides.
- `ModUpdateVM.cs` — Adjusted update workflow to allow folder overrides.
- `ModInstallerPage.xaml` — Added visual options for overwriting and setting folder overrides.

---

## Key Technical Details
- When a user drops a mod onto a character, the installer checks if there's already a directory associated with that mod.
- If there is a name collision or the folder name is different but targets the same entity, the dialog shows an override option where the user can choose to overwrite the old folder or merge/extract to a new name.
