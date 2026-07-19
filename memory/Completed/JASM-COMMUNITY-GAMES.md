# JASM-COMMUNITY-GAMES: Support Remote Git-based Game Asset Sources

**Tracker**: `JASM-COMMUNITY-GAMES`
**Epic**: Custom JASM Features & Release Support
**Status**: Completed (Merged)
**Branch**: `feature/community-game-source`

---

## Summary
Introduced support for loading game assets and definitions from a remote Git repository (Community Games). This allows users to download and update databases of characters, elements, and NPCs for unsupported or community-maintained games without requiring app re-compilation.

---

## Related Tasks
- Feeds into: `JASM-ENDFIELD` (which was enabled via community games format).

---

## What Was Built

### New Files
- `CommunityGamesService.cs` — Added service to fetch, clone, and verify integrity of Git-based remote game asset directories.

### Modified Files
- `GIMI-ModManager.Core.csproj` — Added packages required for remote cloning.
- `FirstTimeStartupActivationHandler.cs` — Adjusted activation flow to check and load community game structures on start.
- `App.xaml.cs` — Registered the `CommunityGamesService`.
- `ModManagerOptions.cs` — Added Options for game sources (Community vs Built-in).
- `Settings.resw` (es-ar, ru-ru, zh-cn) — Added localization strings for Git source management.
- `SettingsViewModel.cs` — Handled remote source checking, repository URL config, and updating actions.
- `StartupViewModel.cs` — Handled community game verification on setup.
- `SettingsPage.xaml` — Added UI for community game source updates and settings.

---

## Key Technical Details
- Community games assets are cloned/stored under the special special SpecialFolder directory: `%localappdata%\JASM\CommunityGames`.
- Integrates Git repo verification, ensuring that required JSON files (like `game.json`, `characters.json`) exist in the cloned directories before switching game profiles.
