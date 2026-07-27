# JASM-JA-LOCALIZE — Add Japanese Language Locale

**Tracker**: JASM-JA-LOCALIZE
**Epic**: Custom JASM Features & Release Support
**Status**: In Progress
**Branch**: `zurce/JASM-JA-LOCALIZE-japanese-localization`

---

## Summary

Add a new Japanese (ja) locale to JASM. This includes creating `Strings/ja/` with `Resources.resw` (853 entries), `Settings.resw` (24 entries), and `Startup.resw` (13 entries), translating all strings from English to Japanese using the CSV batch workflow.

---

## Related Tasks
- **JASM-LOCALIZE** — Initial localization pass that established the en-us source of truth and UID system
- **JASM-LOCALIZE-ITERATION** — Iterating on existing locales (zh-cn, ru-ru, es) — this task adds a new locale

---

## Current State

### Files Created

| File | Entries | Status |
|------|---------|--------|
| `Strings/ja/Resources.resw` | 853 | English values (ready for translation) |
| `Strings/ja/Settings.resw` | 24 | English values (ready for translation) |
| `Strings/ja/Startup.resw` | 13 | English values (ready for translation) |

### Translation Templates Generated

| Template | Location | Entries |
|----------|----------|---------|
| Main Resources CSV | `C:/temp/ja_translations.csv` | 853 |
| Settings CSV | `C:/temp/ja_settings_translations.csv` | 20 |
| Startup CSV | `C:/temp/ja_startup_translations.csv` | 9 |
| Resource batches (57×15) | `C:/temp/ja_batches/ja_batch_*.csv` | 15 ea (last: 13) |

### Scripts Created

| Script | Purpose |
|--------|---------|
| `_scripts/generate_ja_csv.py` | Generate main CSV with Key, English, Context Comment, Spanish (reference), Japanese columns |
| `_scripts/split_ja_batches.py` | Split main CSV into 15-row batches at `C:/temp/ja_batches/` |
| `_scripts/apply_ja_csv.py` | Apply translated batches back to `ja/Resources.resw` |
| `_scripts/generate_ja_base.py` | Generate initial `ja/Resources.resw` from en-us with English values |
| `_scripts/generate_ja_settings_csv.py` | Generate English-sourced Settings & Startup CSVs and .resw files |

---

## Implementation Plan

### Phase 1: Setup ✅ (Complete)
- [x] Create `Strings/ja/` directory
- [x] Generate `ja/Resources.resw` from en-us (853 entries, English values)
- [x] Generate English-sourced `ja/Settings.resw` (24 entries) and `ja/Startup.resw` (13 entries)
- [x] Generate translation CSV templates and split into 57 batches + 2 extra CSVs

### Phase 2: Translate ✅
- [x] Translated all 57 batches (853 entries) — Japanese column filled
- [x] Translated Settings.resw (20 entries)
- [x] Translated Startup.resw (9 entries)
- [x] Rules followed: `{0}`, `{1}` placeholders preserved, `{{TargetPath}}` preserved, brand names/URLs unchanged

### Phase 3: Apply Translations ✅
- [x] Ran `python _scripts/apply_ja_csv.py` — 853 translations applied to `ja/Resources.resw`
- [x] Applied Settings translations to `ja/Settings.resw` (20 entries)
- [x] Applied Startup translations to `ja/Startup.resw` (9 entries)

### Phase 4: Verify ✅
- [x] `dotnet build` — **0 errors**, 140 warnings (PRI263 expected for new locale)
- [ ] Launch app and verify Japanese locale appears in language selector (requires running the app)
- [ ] Spot-check navigation, settings, and character pages in Japanese (requires running the app)

---

## Key Technical Details

### Locale Code
- **ja** (no region suffix) — covers all Japanese variants
- Matches the format used by other locales: `es` (not es-ES), `pt-br`, `zh-cn`, `ru-ru`

### Translation Rules
- Keep `{0}`, `{1}` placeholders exactly as they appear (C# format strings)
- Keep `{{TargetPath}}` and other template tags intact
- Keep URLs and file paths unchanged
- Keep brand names (GameBanana, JASM, 3DMigoto, Genshin Impact, Honkai Star Rail, etc.) unchanged
- Use the Spanish column as tone/style reference for UI string conventions

### Key Format Awareness
- **Underscore format** (`Key_Property`): Used for C# `GetLocalizedStringOrDefault()` calls
- **Dot format** (`Key.Property`): Used for XAML `l:Uids.Uid="Key"` resolution
- Both formats exist in the resw and must be preserved

---

## Deferred Work
- Testing specific edge cases (long strings, Unicode characters in UI)
- Adding more detailed context comments to entries that lack them

---

**Last Updated:** 2026-07-26

**Build:** `dotnet build` — 0 errors, 140 warnings (PRI263 expected for new locale)
