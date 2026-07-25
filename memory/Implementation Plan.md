# Elevator Cleanup and Removal — Implementation Plan

> **Note:** Implementation plans often become outdated as the project evolves. When this happens, mark this document as archival and point to the Knowledge file for current status and architecture.

**Author:** zurce
**Date:** 2026-07-24
**Status:** Draft
**Epic:** Custom Feature Enhancements and Release Stability
**Branch:** `elevator-cleanup-and-removal`
**Sub-agent model:** `deepseek-v4-flash` (via `.pi/settings.json`)

---

## Summary

**Goal:** Remove all elevator-related code, the Elevator project, its DI registrations, UI elements, localization strings, build steps, and documentation references from the JASM codebase.

**Scope:**
- In Scope: Complete removal of the `ElevatorService` class, the `src/Elevator/` project, all `ElevatorService` injections across ViewModels and Services, elevator-related XAML UI elements, localization strings in all languages, build script steps, solution project reference, and README documentation.
- Out of Scope: Refactoring or replacing the functionality provided by the elevator (F10 refresh, auto-sync 3DMigoto config). These become manual-only processes.

**Timeline:** 1 session

---

## Problem Statement

**Current State:**
JASM includes an "Elevator" — a separate elevated (admin) process (`Elevator.exe`) that communicates with the JASM main process via Named Pipes to send F10 key presses to running games, refreshing 3DMigoto mods without requiring the user to alt-tab. The Elevator is:
- A separate C# project under `src/Elevator/`
- Registered in the solution file
- Built and packaged by the release script
- Managed by `ElevatorService` which starts/stops the process and sends commands via Named Pipes
- Injected into 9 different ViewModels and Services
- Featured in the Settings page and Preset page UI
- Localized across 5 language resource files

**Desired State:**
All elevator code and references are completely removed. The auto-refresh (F10) functionality is removed. Users will need to alt-tab to their game and press F10 manually. The "Auto Sync" toggle and elevator status UI are removed from the Preset and Settings pages.

---

## Architecture Overview

### Files to Delete (Complete Removal)

| File/Directory | Reason |
|---|---|
| `src/Elevator/` (entire directory) | The Elevator project — no longer needed |
| `src/GIMI-ModManager.WinUI/Services/ElevatorService.cs` | The service managing elevator lifecycle and IPC |

### Files to Modify

#### Solution & Build

| File | Change |
|---|---|
| `src/GIMI-ModManager.sln` | Remove `Elevator` project entry (`{3C239075-C121-4DDB-88C5-B181863797B7}`) |
| `Build/Release.py` | Remove all elevator build/publish/copy steps (lines 8-9, 22, 46-54, 80-84) |

#### DI & Startup

| File | Change |
|---|---|
| `src/GIMI-ModManager.WinUI/App.xaml.cs` | Remove `services.AddSingleton<ElevatorService>()` (line 131) |
| `src/GIMI-ModManager.WinUI/Services/ActivationService.cs` | Remove `_elevatorService` field, constructor parameter, and `_elevatorService.Initialize()` call in `StartupAsync()` |

#### Services (Injecting ElevatorService)

| File | Change |
|---|---|
| `src/GIMI-ModManager.WinUI/Services/ModRandomizationService.cs` | Remove `_elevatorService` field, constructor param, and `RefreshGenshinMods()` call (lines 199-201) |
| `src/GIMI-ModManager.WinUI/Services/ModHandling/ModPresetHandlerService.cs` | Remove `_elevatorService` field, constructor param, and all `RefreshGenshinMods()` / `RefreshAndWaitForUserIniChangesAsync()` calls + the `_elevatorService.CheckStatus()` check |

#### ViewModels (Injecting ElevatorService)

| File | Change |
|---|---|
| `src/GIMI-ModManager.WinUI/ViewModels/ShellViewModel.cs` | Remove `ElevatorService` property, constructor param, and F10 key handler that calls `RefreshGenshinMods()` (lines 109-116) |
| `src/GIMI-ModManager.WinUI/ViewModels/SettingsViewModel.cs` | Remove `ElevatorService` field, constructor param, `StartElevatorCommand`, `CanStartElevator()`, `_showElevatorStartDialog`, and the entire `StartElevator()` method with its dialog logic |
| `src/GIMI-ModManager.WinUI/ViewModels/PresetViewModel.cs` | Remove `ElevatorService` field/property, constructor param, `StartElevator()` method, `ElevatorIsRunning` property, `ElevatorStatusChangedHandler`, and all `RefreshGenshinMods()` / `RefreshAndWaitForUserIniChangesAsync()` calls. Simplify `AutoSync3DMigotoConfig` logic. |
| `src/GIMI-ModManager.WinUI/ViewModels/PresetDetailsViewModel.cs` | Remove `_elevatorService` field, constructor param, and `RefreshAndWaitForUserIniChangesAsync()` call |
| `src/GIMI-ModManager.WinUI/ViewModels/CharactersViewModel.cs` | Remove `ElevatorService` property, constructor param, `PropertyChanged` handler, and the `RefreshMods` command's elevator-dependent CanExecute logic |
| `src/GIMI-ModManager.WinUI/ViewModels/CharacterGalleryViewModels/CharacterGalleryViewModel.cs` | Remove `_elevatorService` field and constructor param |
| `src/GIMI-ModManager.WinUI/ViewModels/CharacterGalleryViewModels/Commands/ToggleModCommand.cs` | Remove `_elevatorService` field and `RefreshGenshinMods()` call |

#### XAML Views

| File | Change |
|---|---|
| `src/GIMI-ModManager.WinUI/Views/Settings/SettingsPage.xaml` | Remove the entire "Elevator Process" section (header `ElevatorSectionHeader`, status text block, start button `StartElevatorButton`) |
| `src/GIMI-ModManager.WinUI/Views/PresetPage.xaml` | Remove "Start Elevator..." button, elevator-related ToolTip text, and any bindings to `ElevatorService.CanStartElevator` |
| `src/GIMI-ModManager.WinUI/Views/PresetPage.xaml.cs` | Remove elevator-related string from code-behind info text |

#### Localization Files

| File | Keys to Remove |
|---|---|
| `src/GIMI-ModManager.WinUI/Strings/en-us/Resources.resw` | `PresetPage_StartElevatorButton.Content`, `Settings_Elevator_UnableToStart`, `Settings_StartElevator_CloseButton`, `Settings_StartElevator_PrimaryButton`, `Shell_ElevatorNotRunningTitle`, `Shell_ElevatorNotRunningMessage`, `Preset_FailedStartElevator`, `Preset_ElevatorFailed`, plus the elevator description string |
| `src/GIMI-ModManager.WinUI/Strings/es-ar/Resources.resw` | Same set of keys as en-us |
| `src/GIMI-ModManager.WinUI/Strings/es-ar/Settings.resw` | `ElevatorSectionHeader.Text`, `ElevatorStatusTitle.Text`, `StartElevatorButton.Content`, `StartElevatorDialogDontShowContent`, `StartElevatorDialogText`, `StartElevatorDialogTitle` |
| `src/GIMI-ModManager.WinUI/Strings/zh-cn/Settings.resw` | Same Settings keys as es-ar |
| `src/GIMI-ModManager.WinUI/Strings/ru-ru/Settings.resw` | Same Settings keys as es-ar |
| `src/GIMI-ModManager.WinUI/Strings/es-ar/Resources.resw.bak` | Same set as en-us Resources (backup file) |

#### AutoUpdater

| File | Change |
|---|---|
| `src/JASM.AutoUpdater/MainPageVM.cs` | Remove `"Elevator.exe"` from `doNotDeleteFiles` array (line 336) |

#### Documentation

| File | Change |
|---|---|
| `README.md` | Remove "F10 - Refresh Mods" bullet (line 29), remove "### Elevator process" section (lines 52-53), remove "### Elevator download link" section (lines 120-122) |
| `memory/[Knowledge] JASM.md` | Update Overview/Goals to remove elevator mention. Update architecture description. |

---

## Implementation Phases

### Phase 1: Core Removal (the deletion)

**Deliverables:**
- [ ] Delete `src/Elevator/` directory
- [ ] Delete `src/GIMI-ModManager.WinUI/Services/ElevatorService.cs`
- [ ] Remove Elevator project from `src/GIMI-ModManager.sln`
- [ ] Remove DI registration from `App.xaml.cs`
- [ ] Remove elevator from `ActivationService.cs` (field, constructor, Initialize call)

### Phase 2: Service & ViewModel Cleanup

**Deliverables:**
- [ ] Clean `ModRandomizationService.cs` — remove elevator field, param, and Refresh call
- [ ] Clean `ModPresetHandlerService.cs` — remove elevator field, param, and all Refresh calls
- [ ] Clean `ShellViewModel.cs` — remove property, param, F10 handler
- [ ] Clean `SettingsViewModel.cs` — remove field, param, StartElevator command, dialog
- [ ] Clean `PresetViewModel.cs` — remove field, param, StartElevator, ElevatorIsRunning, auto-sync, handlers
- [ ] Clean `PresetDetailsViewModel.cs` — remove field, param, Refresh call
- [ ] Clean `CharactersViewModel.cs` — remove property, param, handler, CanExecute check
- [ ] Clean `CharacterGalleryViewModel.cs` — remove field, param
- [ ] Clean `ToggleModCommand.cs` — remove field, Refresh call

### Phase 3: UI & Localization Cleanup

**Deliverables:**
- [ ] Clean `SettingsPage.xaml` — remove elevator section
- [ ] Clean `PresetPage.xaml` — remove Start Elevator button and bindings
- [ ] Clean `PresetPage.xaml.cs` — remove elevator string from info text
- [ ] Clean all 5 `*.resw` files — remove elevator localization keys
- [ ] Clean `Resources.resw.bak` — remove elevator keys

### Phase 4: Build, AutoUpdater & Docs

**Deliverables:**
- [ ] Clean `Build/Release.py` — remove elevator build/copy steps
- [ ] Clean `MainPageVM.cs` — remove Elevator.exe from doNotDeleteFiles
- [ ] Update `README.md` — remove elevator sections
- [ ] Update `memory/[Knowledge] JASM.md` — update goals/architecture

### Phase 5: Verification

**Deliverables:**
- [ ] Build succeeds: `dotnet build src\GIMI-ModManager.WinUI\GIMI-ModManager.WinUI.csproj -p:Platform=x64`
- [ ] No remaining references: `rg -i "elevator" src/` returns zero results
- [ ] App launches without errors

---

## Testing Strategy

### Build Verification
- Clean + Build the WinUI project with `x64` platform
- Verify zero elevator-related compilation errors or warnings

### Manual Testing
- Launch JASM and verify it starts without DI resolution errors
- Navigate to Settings page — no elevator section visible
- Navigate to Preset page — no Start Elevator button, no auto-sync elevator-dependent controls
- Verify F10 key does nothing (no elevator-related notification)
- Enable/disable mods — verify no crash from missing elevator service calls

---

## Risks

| Risk | Mitigation |
|---|---|
| Removing `RefreshGenshinMods()` calls could leave dead code paths in `ModPresetHandlerService` | Review the `CanAutoSync` / auto-sync logic carefully — if `_elevatorService.CheckStatus()` was the sole gate, remove the entire auto-sync branch |
| `PresetViewModel` has complex state tied to `ElevatorIsRunning` and `AutoSync3DMigotoConfig` | Trace all bindings — `AutoSync3DMigotoConfig` may also be bound to XAML. If the auto-sync checkbox exists independently, decide whether to keep or remove it |
| `SettingsViewModel.StartElevator()` has a large dialog method | Remove the entire method and its supporting fields (`_showElevatorStartDialog`) |
| Localization `.resw` files are XML — careful with exact tag boundaries | Use precise text matching; removing one `<data>` block at a time |

---

## References

- ElevatorService: `src/GIMI-ModManager.WinUI/Services/ElevatorService.cs`
- Elevator project: `src/Elevator/`
- Settings elevator UI: `src/GIMI-ModManager.WinUI/Views/Settings/SettingsPage.xaml` (lines 256-283)
- Preset elevator UI: `src/GIMI-ModManager.WinUI/Views/PresetPage.xaml` (lines 119-144)
- Build script: `Build/Release.py`
- Knowledge base: `memory/[Knowledge] JASM.md`

---

**Last Updated:** 2026-07-24
