# JASM-CLOSE-INSTALL: Automatically Close Mod Installer on Success

**Tracker**: `JASM-CLOSE-INSTALL`
**Epic**: Custom JASM Features & Release Support
**Status**: Completed (Merged)
**Branch**: `zurce/close-on-install`

---

## Summary
Improved the mod installation flow by automatically closing the Mod Installer View/Page and returning the user to the previous page once a mod has been successfully installed.

---

## Related Tasks
*None*

---

## What Was Built

### Modified Files
- `ModPageVM.cs` — Invoked navigation back command upon successful installation confirmation event.

---

## Key Technical Details
- Added callback handler inside `ModPageVM` to listen to the end of the installation extraction pipeline.
- If the installation succeeds with zero errors, it calls the navigation service to automatically pop the page stack and go back.
