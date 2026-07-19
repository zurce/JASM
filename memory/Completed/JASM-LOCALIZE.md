# JASM-LOCALIZE: Localize All Missing Strings

**Tracker**: `JASM-LOCALIZE`
**Epic**: Custom JASM Features & Release Support
**Status**: Completed
**Branch**: `zurce/JASM-LOCALIZE-app-localization`

---

## Summary
Localize all hardcoded English strings across the XAML views using the existing WinUI3Localizer framework. Many strings are currently hardcoded instead of going through the `l:Uids.Uid` attached property and .resw resource files. All 4 locales (en-us, zh-cn, ru-ru, es-ar) need complete coverage.

## Related Tasks
- None directly, but touches files modified in `JASM-BATCH-CONFIG` and `JASM-CHAR-COMMANDS` (those added new XAML strings that need localization).

## What Was Built

### New Files
- `Strings/en-us/Resources.resw` — Extended with 130+ entries covering all major UI pages

### Modified Files
- `Views/ShellPage.xaml` — Added xmlns:l + navigation item UIDs (3 of 5)
- `Views/CharactersPage.xaml` — Added xmlns:l + ~20 UIDs (menu items, dialogs, batch configs, filters)
- `Views/CharacterDetailsPages/CharacterDetailsPage.xaml` — Added xmlns:l + ~15 UIDs (command expander, menu bar, view toggle)
- `Views/CharacterDetailsPages/ModPane.xaml` — Added xmlns:l + 7 UIDs (mod url, keys, save, status)
- `Views/PresetPage.xaml` — Added xmlns:l + ~20 UIDs (presets, sync, elevator, randomize, reset)
- `Views/ModInstallerPage.xaml` — Added xmlns:l + 21 UIDs (folders, naming, overwrite, add mod)

### Already Localized from Original Codebase
- CharacterGalleryPage, CharacterCard, ErrorWindow, GbModPageWindow, NotificationsPage, DebugPage, ModListOverview — already had UIDs
- SettingsPage — partially localized (30+ existing UIDs)
- StartupPage — fully localized
- ModsOverviewPage — partially localized

### Tests
- Build verification: `dotnet build -p:Platform=x64` passes with 0 errors

## Key Technical Details

### Localization System
- Framework: WinUI3Localizer with `l:Uids.Uid` attached property
- Resource files: `src/GIMI-ModManager.WinUI/Strings/{locale}/*.resw`
- 4 supported locales: en-us (fallback), zh-cn, ru-ru, es-ar
- The UID maps to .resw entries with property suffix (e.g., `MyUid.Text`, `MyUid.Content`)

### Approach Used
- XAML text stays as-is (serves as fallback)
- UIDs added alongside existing attributes
- PowerShell regex used for bulk replacements to avoid manual edit errors

## Key Technical Details

### Localization Framework
- Uses **WinUI3Localizer** with `l:Uids.Uid` attached property (NOT `x:Uid`)
- Resource files in `src/GIMI-ModManager.WinUI/Strings/{locale}/`
- 4 locales: `en-us` (fallback), `zh-cn`, `ru-ru`, `es-ar`
- Files per locale: `Resources.resw`, `Settings.resw`, `Startup.resw`
- UID convention: `PageName_ElementName.Property` (e.g., `Characters_RefreshMods.Content`)

### Strategy
1. For each XAML element with hardcoded text, replace literal text with `l:Uids.Uid="SomeUid"` 
2. Add string entries to `en-us/Resources.resw` (or page-specific .resw files)
3. The en-us strings serve as the source-of-truth keys AND values
4. Other locales will receive translated values later (or use en-us as fallback)

### Scope — Pages to Localize (in priority order)

1. **ShellPage.xaml** — 5 strings (nav menu items)
2. **CharactersPage.xaml** — ~45 strings (batch configs, dialogs, filters, sort)
3. **CharacterDetailsPage.xaml** — ~35 strings (move, override, commands, mods, folders, view)
4. **CharacterCard.xaml** — ~6 strings (tooltips, stats, buttons)
5. **CharacterGalleryPage.xaml** — ~20 strings (sort, view, search, image/delete actions)
6. **ModPane.xaml** — ~17 strings (display name, ini, keyswaps, url, save)
7. **ModsOverviewPage.xaml** — ~13 strings (commands, folders, search)
8. **ModInstallerPage.xaml** — ~35 strings (folders, naming, overwrite, preview)
9. **PresetPage.xaml** — ~35 strings (sync, elevator, presets, randomize, reset)
10. **PresetDetailsPage.xaml** — ~10 strings (search, add mod, replace, remove)
11. **NotificationsPage.xaml** — ~3 strings
12. **ErrorWindow.xaml** — ~4 strings
13. **CommandsSettingsPage.xaml** — ~10 strings
14. **CreateCommandView.xaml** — ~15 strings
15. **GbModPageWindow.xaml** — ~9 strings
16. **ModUpdateAvailableWindow.xaml** — ~8 strings
17. **ModSelector.xaml** — ~4 strings
18. **ModListOverview.xaml** — ~3 strings
19. **CharacterManagerPage.xaml** — ~3 strings
20. **DebugPage.xaml** — ~3 strings

**EasterEggPage.xaml** — intentionally NOT localized (joke strings).

### Known Locale Gaps
- **ru-ru/Resources.resw**: only 5 strings (vs 34+ needed). Needs full population.
- **en-us** only has `Resources.resw` — needs `Settings.resw` and `Startup.resw` files or all strings should go in `Resources.resw`.

## Key Learnings
- PowerShell regex approach for XAML edits is more reliable than the edit tool for bulk changes, but requires careful namespace handling per file
- WinUI3Localizer .resw entries use `.Text`, `.Content`, `.Header`, `.Title`, `.PlaceholderText`, `.Label`, `.OffContent`, `.OnContent`, `.Message` as property suffixes
- The `TargetPath` UID contains `{{` which must be escaped in PowerShell

## Review Feedback Addressed

### Bug Fix: ContentDialog resw keys had wrong format (amended in commit `f270204`)

**Issue:** 4 ContentDialogs (`SelectProcessDialog`, `EnableAllDialog`, `DisableAllDialog`, `CleanUpDialog`) had resw keys named `..._Title.Title` but the XAML UID was `...Dialog`, so WinUI3Localizer looked for `...Dialog.Title`. The `_Title` infix broke matching — title localization silently fell back to English.

**Fix:**
- Renamed `_Title.Title` → `.Title` in `en-us/Resources.resw` for all 4 dialogs
- Added `l:Uids.Uid` to the `<TextBlock>` children inside those dialogs (they were unlocalizable)
- The `_Description.Text` resw entries were already present — they now match the XAML UIDs
- Fixed typo: `"configurtation"` → `"configuration"` (in both XAML fallback and resw)
- es-ar already had correct `.Title` keys; no changes needed there

## Deferred Work
- Remaining ShellPage navigation items (Debug, Notifications) — multi-line elements need manual UID addition
- Translations for zh-cn, ru-ru, es-ar locales — currently only en-us entries exist; translators needed
- SettingsPage remaining hardcoded strings (Window size, Commands card, cache text, update section)
- CommandsSettingsPage.xaml and CreateCommandView.xaml — not yet UID-ized
- CharacterManagerPage.xaml — not yet UID-ized (had duplicate UID issue, needs careful handling)
- ModUpdateAvailableWindow.xaml — partially UID-ized, 8 strings remaining
