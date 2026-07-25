# JASM-LOCALIZE-ITERATION — Iterate on Translations for Release

**Tracker**: JASM-LOCALIZE-ITERATION
**Epic**: Custom JASM Features & Release Support
**Status**: In Progress
**Branch**: `zurce/JASM-LOCALIZE-ITERATION-translation-iteration`

---

## Summary
Populate missing translations in zh-cn and ru-ru resource files, fix es-ar gaps, ensure all programmatic (notification) strings are covered, and validate end-to-end localization against the Release build output.

---

## Related Tasks
- **Previous work**: `JASM-LOCALIZE` — Initial localization pass that added UIDs to XAML and created en-us entries but deferred translation population
- **Deferred FROM JASM-LOCALIZE**:
  - Translations for zh-cn, ru-ru, es-ar locales — only en-us entries existed
  - Remaining ShellPage navigation items (Debug, Notifications)
  - SettingsPage remaining hardcoded strings (Window size, Commands card, cache text, update section)
  - CommandsSettingsPage.xaml and CreateCommandView.xaml — not yet UID-ized
  - CharacterManagerPage.xaml — not yet UID-ized (had duplicate UID issue)
  - ModUpdateAvailableWindow.xaml — partially UID-ized, 8 strings remaining

---

## Current State

### Resource File Coverage

| Locale | Resources.resw | Settings.resw | Startup.resw | Status |
|--------|---------------|--------------|-------------|--------|
| en-us  | 830 entries   | —            | —           | ✅ Source of truth (fallback) |
| es (español) | ~835 entries | 50 entries | 39 entries | ✅ Near complete (was es-ar, renamed to general `es`) |
| zh-cn  | **62 entries** | 50 entries  | 39 entries  | ❌ ~768 missing from Resources.resw |
| ru-ru  | **35 entries** | 50 entries  | 39 entries  | ❌ ~795 missing from Resources.resw |

### What's Missing

**zh-cn**: All notification strings (98+ entries), all Character Details strings, all Shell navigation, Settings strings in Resources.resw, Mod Installer strings, Commands strings, Preset strings — essentially the entire bulk of XAML localization.

**ru-ru**: Same as zh-cn — only 35 entries exist (AppDisplayName, Category names, some Startup remnants).

**es (español)**: Near parity with en-us but may have a few gaps where new keys were added during JASM-LOCALIZE iteration. Previously `es-ar` — renamed to general `es` to cover all Spanish locales.

---

## Scope

### In Scope
1. **Translate zh-cn/Resources.resw** — populate all ~768 missing entries with Chinese translations
2. **Translate ru-ru/Resources.resw** — populate all ~795 missing entries with Russian translations
3. **Verify es/Resources.resw** — check for any missing keys vs en-us and add them
4. **Check for hardcoded strings** — scan XAML for any remaining hardcoded English text that lacks `l:Uids.Uid`
5. **Address JASM-LOCALIZE deferred items** — SettingsPage, CommandsSettingsPage, CreateCommandView, CharacterManagerPage, ModUpdateAvailableWindow
6. **Verify against Release build** — build with Release configuration and confirm no missing string runtime fallbacks

### Out of Scope
- Adding new locales (e.g., ja-JP, ko-KR, pt-BR) — only the existing 4 locales
- Rewriting the localization framework
- Removing en-us fallback behavior

---

## Translation Sources
For machine translation where exact equivalents don't exist:
- **zh-cn**: Use Simplified Chinese, maintain existing UIDs from en-us
- **ru-ru**: Use Russian (Cyrillic), maintain existing UIDs from en-us
- Preserve existing winui3localizer conventions (`.Text`, `.Content`, `.Header`, `.Title`, `.PlaceholderText`, `.Label`, `.OffContent`, `.OnContent`, `.Message` suffixes)

---

## Key Technical Details

### Localization Framework
- **WinUI3Localizer** with `l:Uids.Uid` attached property
- **en-us** is the fallback language — if a key is missing in the current locale, en-us value is used
- All resource files are `.resw` XML format (standard WinUI resource files)
- The `Localizer.cs` service loads all `.resw` files from the `Strings/` folder at startup

### UID Convention
- `PageName_ElementName.Property` (e.g., `Characters_RefreshMods.Content`)
- UIDs must match exactly between XAML and `.resw` files
- ContentDialogs: the UID on the `<ContentDialog>` element itself uses `.Title`; child elements (TextBlock, etc.) need their own UIDs

### Notification Strings
- All programmatic notifications use `Notification_*` prefix
- Called via `GetLocalizedStringOrDefault("Notification_*")` in C# ViewModels/Services
- These are critical — missing keys silently fall back to en-us but should be translated

---

## Implementation Strategy

### Phase 1: Analyze & Baseline
- [ ] Compare en-us keys vs zh-cn, ru-ru, es-ar to get exact missing-key list
- [ ] Scan XAML for hardcoded strings without UIDs
- [ ] Identify all notification string usages in C# code

### Phase 2: Populate zh-cn and ru-ru
- [ ] Add all missing `Notification_*` entries to zh-cn/Resources.resw
- [ ] Add all missing UI string entries to zh-cn/Resources.resw
- [ ] Add all missing `Notification_*` entries to ru-ru/Resources.resw
- [ ] Add all missing UI string entries to ru-ru/Resources.resw

### Phase 3: Fill es (Spanish) gaps
- [X] Rename es-ar → es (general Spanish, not Argentina-specific)
- [ ] Check es against en-us for any missing keys
- [ ] Add any missing entries

### Phase 4: Deduplication & Redundancy Cleanup
- [X] Rename es-ar → es (general Spanish)
- [X] Remove duplicate key entries (same key appearing twice) in es/Resources.resw and en-us/Resources.resw:
  - `CharacterDetails_MultipleModsActiveMessage` — removed duplicate (line 2509 in es, 2419 in en-us)
  - `CharacterDetails_OpenCharacterModFolder.Text` — removed duplicate (line 2533 in es, 2443 in en-us)
- [X] Removed 12 dead underscore-style duplicate keys from en-us/Resources.resw and 14 from es/Resources.resw. These had `Key.Property` (correct dot format, used by XAML UID resolution) and `Key_Property` (underscore format, dead — no C# or XAML references them). E.g.: `CharacterDetails_ViewToggle_OffContent`, `CreateCommandView_Arguments_PlaceholderText`, `ModInstallerPage_Note_PlaceholderText`
- [X] Fixed `CharactersPage_CleanUpDialog.Title` typo in en-us: "Clean up disable mods?" → "Clean up disabled mods?"
- [ ] Resolve C#-referenced keys missing from resw (26 keys, e.g. `Notification_AnErrorOccurred`, `CharDetails_DragDropFailed`, `Settings_Mods_ReorganizeFailed`)
- [ ] Add resw entries for 36 XAML UIDs that have no matching resw key (e.g. `/Settings/GameSelectorComboBox`, `/Startup/Startup_Header`, `CharacterDetails_LoadingText`, `ModInstallerPage_AlwaysOnTopToggle`)

### Phase 5: Hardcoded String Cleanup
- [ ] Address deferred items from JASM-LOCALIZE:
  - [ ] SettingsPage remaining strings (Window size, Commands card, cache, update)
  - [ ] CommandsSettingsPage.xaml — add UIDs
  - [ ] CreateCommandView.xaml — add UIDs
  - [ ] CharacterManagerPage.xaml — add UIDs (handle duplicate UID issue)
  - [ ] ModUpdateAvailableWindow.xaml — finish remaining 8 strings
  - [ ] ShellPage Debug/Notifications nav items
- [ ] Scan for any other hardcoded strings

### Phase 6: Validation
- [ ] Build with `dotnet build -p:Platform=x64` — 0 errors
- [ ] Verify app launches and navigates without crashes
- [ ] Spot-check zh-cn and ru-ru UI in a test session

---

## Deferred Work
- Adding new language locales — if desired, create separate task
- Deep localization of EasterEggPage.xaml — intentionally not localized (joke strings)

---

**Last Updated:** 2026-07-25 (Phase 4 complete: deduplication, ex-ar→es rename, typo fix)
