# EPIC: Custom JASM Features & Release Support

This file mirrors active tasks and development phases.

---

## Completed

- **`JASM-JA-LOCALIZE`** — Add Japanese language locale: created ja/Resources.resw (853), Settings.resw (24), Startup.resw (13) with full Japanese translations (branch: `zurce/JASM-JA-LOCALIZE-japanese-localization`, merged)
- **`JASM-RU-LOCALIZE`** — Populated Russian (ru-ru) locale: translated Resources.resw (853), Settings.resw (24), Startup.resw (13) to Russian (branch: `zurce/JASM-RU-LOCALIZE-russian-localization`, merged)
- **`JASM-ZH-LOCALIZE`** — Populated Chinese (zh-cn) locale: translated Resources.resw from 47 to 853 entries (branch: `zurce/JASM-ZH-LOCALIZE-chinese-localization`, merged)
- **`JASM-MIGRATE-CLI`** — Migrated JASM patterns, architecture conventions, and development workflows to `antigravity-cli`. (pattern transfer)
- **`JASM-ELEVATOR-CLEANUP`** — Remove all elevator-related code: Elevator project, ElevatorService, UI, localization, build steps, and docs. (branch: `elevator-cleanup-and-removal`)
- **`JASM-LOCALIZE`** — Localize all missing/hardcoded English strings across XAML views using WinUI3Localizer (committed, pushed, amended `f270204` with review fixes).
- **`JASM-BATCH-CONFIG`** — Implement Batch Configurations (Enable all, Disable all, Clean up mods) on Characters Overview (committed, pushed).
- **`JASM-OVERRIDE`** — Support for overriding folder settings (merged).
- **`JASM-COMMUNITY-GAMES`** — Support remote Git-based loading of game assets / community game sources (merged).
- **`JASM-CHAR-COMMANDS`** — Support running custom commands directly from Character Details context menu (merged).
- **`JASM-SPACES`** — Fixed spaces handling when substituting `{{TargetPath}}` in custom commands (merged).
- **`JASM-CLOSE-INSTALL`** — Automatically close the Mod Installer page after a mod successfully installs (merged).
- **`JASM-ENDFIELD`** — Added initial support for Arknights: Endfield (merged).

---

## In Progress

- **`JASM-LOCALIZE-ITERATION`** — Iterate on translations: rename es-ar → es (general Spanish), populate zh-cn and ru-ru resource files, fix any gaps across all locales, validate Release build string coverage

---

## Ready to Do

- **`JASM-TESTS`** — Implement automated integration tests for mod directory enabling/disabling states.

---

## Blocked

*None*
