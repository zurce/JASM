# JASM-SPACES: Quote Parameter Values with Spaces in Commands

**Tracker**: `JASM-SPACES`
**Epic**: Custom JASM Features & Release Support
**Status**: Completed (Merged)
**Branch**: `zurce/fix-targetpath-spaces`

---

## Summary
Fixed an issue where custom commands executing against a file path containing spaces (e.g. `C:\My Mods\Cool Skin`) would crash or fail because parameters were not quoted during variable substitution.

---

## Related Tasks
- Depends on: `JASM-CHAR-COMMANDS` (which passes file paths dynamically to commands).

---

## What Was Built

### Modified Files
- `CommandService.cs` — Passed `quoteValuesWithSpaces: true` into the `ReplaceVariables` helper.
- `SpecialVariables.cs` — Added quoting utility logic inside `ReplaceVariables` to automatically wrap parameter strings in double quotes if they contain spaces.

---

## Key Technical Details
- Added a `quoteValuesWithSpaces` boolean parameter to `ReplaceVariables(string input, SpecialVariablesInput variables, bool quoteValuesWithSpaces)`.
- If `true` and the variable value has spaces, the replacement inserts escaped quotes around the value (e.g., `\"C:\My Mods\Cool Skin\"`), preventing command line interpreters from splitting the path into separate arguments.
