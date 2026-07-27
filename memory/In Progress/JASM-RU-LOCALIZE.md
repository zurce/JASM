# JASM-RU-LOCALIZE — Populate Russian Language Locale

**Tracker**: JASM-RU-LOCALIZE
**Epic**: Custom JASM Features & Release Support
**Status**: In Progress
**Branch**: `zurce/JASM-RU-LOCALIZE-russian-localization`

---

## Summary

Populate the Russian (ru-ru) locale with full translations. Previously ru-ru only had 5 actual translations; this brings it to parity with en-us: Resources.resw (853 entries), Settings.resw (24), Startup.resw (13).

---

## Related Tasks
- **JASM-LOCALIZE-ITERATION** — Original task that planned ru-ru population but never completed it
- **JASM-JA-LOCALIZE** — Followed the same workflow for Japanese

---

## Current State

| File | Entries | Translated | English |
|------|---------|------------|---------|
| `Strings/ru-ru/Resources.resw` | 853 | 5 (preserved) | 848 |
| `Strings/ru-ru/Settings.resw` | 24 | 0 | 24 |
| `Strings/ru-ru/Startup.resw` | 13 | 0 | 13 |

### Translation Templates Generated

| Template | Location | Entries |
|----------|----------|---------|
| Main Resources CSV | `C:/temp/ru_translations.csv` | 853 |
| Main batches (57×15) | `C:/temp/ru_batches/ru_batch_*.csv` | 15 ea (last: 13) |
| Settings CSV | `C:/temp/ru_settings_translations.csv` | 20 |
| Startup CSV | `C:/temp/ru_startup_translations.csv` | 9 |

### Scripts Created

| Script | Purpose |
|--------|---------|
| `_scripts/generate_ru_csv.py` | Generate main CSV |
| `_scripts/split_ru_batches.py` | Split into 15-row batches |
| `_scripts/apply_ru_csv.py` | Apply batches to ru-ru/Resources.resw |
| `_scripts/generate_ru_base.py` | Generate initial ru-ru files from en-us |
| `_scripts/generate_ru_settings_csv.py` | Generate Settings & Startup CSVs |

---

## Translation Rules
- Keep `{0}`, `{1}` placeholders exactly as they appear
- Keep `{{TargetPath}}` and template tags intact
- Keep URLs and file paths unchanged
- Keep brand names (GameBanana, JASM, 3DMigoto, Genshin Impact, Honkai Star Rail, etc.) unchanged
- Use Spanish column as tone/style reference
- Use Cyrillic script (Russian)

---

## Implementation Plan

### Phase 1: Setup ✅
- [x] Create branch `zurce/JASM-RU-LOCALIZE-russian-localization`
- [x] Generate ru-ru files from en-us (5 existing translations preserved)
- [x] Generate CSV templates and split into 57 batches + 2 extra CSVs

### Phase 2: Translate ✅
- [x] Translate all 57 batches (853 entries) — Russian column filled
- [x] Translate Settings.resw (20 entries)
- [x] Translate Startup.resw (9 entries)

### Phase 3: Apply
- [ ] Run `python _scripts/apply_ru_csv.py` to apply all batches
- [ ] Apply Settings and Startup translations to .resw files

### Phase 4: Verify
- [ ] Build with `dotnet build` — 0 errors
- [ ] Launch and spot-check

---

**Last Updated:** 2026-07-26
