# Knowledge: JASM - Just Another Skin Manager

**Status:** In Progress
**Start Date:** 2026-02-25
**Owner:** zurce
**Epic:** Custom Feature Enhancements and Release Stability

---

## Overview

JASM (Just Another Skin Manager) is a skin manager for games like Genshin Impact, Honkai Star Rail, Wuthering Waves, Zenless Zone Zero, and Arknights: Endfield. It is built as a Windows desktop application using **WinUI 3** and the **Windows App SDK** in .NET 9.

**Module:** `GIMI-ModManager`
**Scope:** Includes mod management (enabling, disabling, sorting, and cleaning up), preset configurations, process execution for 3DMigoto and games, and custom command execution.
**Architecture:** Clean architecture split into Core (`GIMI-ModManager.Core`) and UI/Presentation (`GIMI-ModManager.WinUI`) layers using CommunityToolkit.Mvvm.

---

## Goals

- Provide a smooth, dark-mode GUI for organizing, installing, and switching skin mods.
- Watch folder structures and react to filesystem events instantly (auto-detection of mods).
- Support advanced batch operations (enable all, disable all, and clean up inactive folders).
- Run side-car processes (like the elevated Elevator tool) to interact with running games (e.g. sending F10 via Named Pipes to refresh skins).
- Allow users to run customizable command-line utilities (like texture upscalers) directly against mod folders.

---

## Existing Implementation Analysis

### Current State
- The codebase is structured into two main projects:
  - **`src/GIMI-ModManager.Core`**: Handles all business logic, games service definitions, filesystem watchers, command definition serialization, and game profiles.
  - **`src/GIMI-ModManager.WinUI`**: Contains views (`Page`, `ContentDialog`, `UserControl`) and viewmodels (`ObservableObject`, `ObservableRecipient`) utilizing the MVVM pattern.
- App settings and profile configs are stored under `%localappdata%\JASM` (e.g. `ApplicationData_Genshin`, `ApplicationData_ZZZ`).
- Localization is handled dynamically by `WinUI3Localizer` with `.resw` resource files located in `src/GIMI-ModManager.WinUI/Strings/`.

### What's Changing
- Over time, custom features have been added to support multi-game setups, remote git-based community game sources, and custom commands shown directly in mod/character menus.
- Recent changes added **Batch Configurations** (Enable all, Disable all, Clean up disabled folders) to the characters list to easily clean up space and manage configs.

---

## Related Projects and Reference Implementations

### Characters ViewModel & Page
**Location:** [CharactersViewModel.cs](file:///c:/Users/zurce/Code/JASM/src/GIMI-ModManager.WinUI/ViewModels/CharactersViewModel.cs) and [CharactersPage.xaml](file:///c:/Users/zurce/Code/JASM/src/GIMI-ModManager.WinUI/Views/CharactersPage.xaml)
- **Why it's relevant:** Serves as the primary template for grids, menus, commands, and handling complex interactions like drag-and-drop or popups.
- **What to copy:** Grid layout, filters binding, and using `App.MainWindow.Content.XamlRoot` to safely show ContentDialogs.

### Command Service & JSON Context
**Location:** [CommandService.cs](file:///c:/Users/zurce/Code/JASM/src/GIMI-ModManager.Core/Services/CommandService/CommandService.cs) and [CommandJsonContext.cs](file:///c:/Users/zurce/Code/JASM/src/GIMI-ModManager.Core/Services/CommandService/JsonModels/CommandJsonContext.cs)
- **Why it's relevant:** Demonstrates how to load/save JSON models robustly in trimmed Release builds using source-generated serialization.

---

## Progress Summary

**Completed: 8 Custom Tasks**

### Completed Tasks

| Task | Description | Status | Date |
|------|-------------|--------|------|
| `JASM-LOCALIZE` | Localize all hardcoded English strings using WinUI3Localizer | Staged & Committed | 2026-07-19 |
| `JASM-BATCH-CONFIG` | Added batch Enable All, Disable All, and Clean Up operations to characters overview. | Staged & Committed | 2026-07-18 |
| `JASM-OVERRIDE` | Merge branch `zurce/add-override-folder` for overriding folder settings. | Merged | 2026-03-05 |
| `JASM-COMMUNITY-GAMES`| Support remote Git-based loading of game assets / community game sources. | Merged | 2026-03-05 |
| `JASM-CHAR-COMMANDS` | Support running custom commands directly from Character Details context menu. | Merged | 2026-03-05 |
| `JASM-SPACES` | Fixed spaces handling when substituting `{{TargetPath}}` in custom commands. | Merged | 2026-02-28 |
| `JASM-CLOSE-INSTALL` | Automatically close the Mod Installer page after a mod successfully installs. | Merged | 2026-02-28 |
| `JASM-ENDFIELD` | Added initial support for Arknights: Endfield. | Merged | 2026-02-25 |

---

## Architecture

### Module Structure (Actual)
```
src/
  GIMI-ModManager.Core/
    Entities/                 # Domain objects (e.g. CharacterModList, SkinMod)
    GamesService/             # Game configuration definitions and assets
    Services/
      CommandService/         # Custom command definitions and execution options
  GIMI-ModManager.WinUI/
    Activation/               # Startup and page routing activation
    Services/                 # Presentation services (e.g. WindowManagerService)
    ViewModels/               # ViewModels for all views and dialogs
    Views/                    # XAML Pages and controls
```

---

## Key Decisions and Learnings

### 2026-03-15: Release Build JSON Serialization & Trimming
When compiling in `Release` mode, the .NET trimmer strips reflection metadata from internal serialization types like `JsonCommandRoot` and `JsonCommandDefinition`. This causes commands to lose fields (like `Arguments` or `WorkingDirectory`) during save/load.
- **Decision:** Introduced a source-generated `CommandJsonContext` class and registered it in `CommandService` to enforce trim-proof compile-time serialization.

### 2026-07-18: WinUI 3 XamlRoot Defensive Coding
When displaying a `ContentDialog` in WinUI 3 (even if declared in XAML), it can crash if its `XamlRoot` is null at invocation time.
- **Decision:** Always assign `dialog.XamlRoot ??= App.MainWindow.Content.XamlRoot;` in VM commands before calling `.ShowAsync()`.

---

**Last Updated:** 2026-07-19
