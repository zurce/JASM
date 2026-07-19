# JASM-BATCH-CONFIG: Batch Enable, Disable, and Clean Up Mods

**Tracker**: `JASM-BATCH-CONFIG`
**Epic**: Custom JASM Features & Release Support
**Status**: Completed (Committed & Pushed)
**Branch**: `release`

---

## Summary
Implements a new "Batch Configurations" menu on the Characters page to allow users to execute mass actions (Enable all mods, Disable all mods, and Clean up unused disabled mod directories) across all active character categories.

---

## Related Tasks
- Depends on: `JASM-CHAR-COMMANDS` (context menu commands layout reference).

---

## What Was Built

### New Files
*None*

### Modified Files
- [ISkinManagerService.cs](file:///c:/Users/zurce/Code/JASM/src/GIMI-ModManager.Core/Contracts/Services/ISkinManagerService.cs) — Added method definitions.
- [SkinManagerService.cs](file:///c:/Users/zurce/Code/JASM/src/GIMI-ModManager.Core/Services/SkinManagerService.cs) — Implemented core enable, disable, and clean up operations.
- [CharactersPage.xaml](file:///c:/Users/zurce/Code/JASM/src/GIMI-ModManager.WinUI/Views/CharactersPage.xaml) — Added Confirmation Dialogs and Dropdown menu buttons.
- [CharactersViewModel.cs](file:///c:/Users/zurce/Code/JASM/src/GIMI-ModManager.WinUI/ViewModels/CharactersViewModel.cs) — Added commands to trigger dialogs and run service actions.
- [DisableAllModsDialog.cs](file:///c:/Users/zurce/Code/JASM/src/GIMI-ModManager.WinUI/ViewModels/SettingsViewModels/DisableAllModsDialog.cs) — Simplified setting-level disable logic to call the central service method.

### Tests
- Manually compiled in x64 Debug mode, validated that dialogs render correctly and filesystem directories are renamed/deleted accordingly when confirmed.

---

## Key Technical Details

### 1. Watcher Pausing During Mass Deletes
Deleting multiple directories named `DISABLED_*` while directory watchers are active can trigger a storm of filesystem events, leading to file access exceptions or slow performance.
- **Solution**: We created a `DisableWatcher` helper inside `CharacterModList` that returns an `IDisposable` to temporarily pause `EnableRaisingEvents = false` for the duration of the deletes.

### 2. Defensive XamlRoot Assignment
Popups shown directly from the ViewModel via ContentDialog parameters can fail if their parent window's visual root is not connected.
- **Solution**: Explicitly set the `XamlRoot` right before calling `.ShowAsync()`:
  ```csharp
  dialog.XamlRoot ??= App.MainWindow.Content.XamlRoot;
  ```

---

## Key Learnings
- In WinUI 3, ContentDialogs declared in XAML are not always auto-populated with `XamlRoot` depending on how they are referenced in VM commands. Standardizing on `dialog.XamlRoot ??= App.MainWindow.Content.XamlRoot` is the safest way to avoid random runtime crashes.
