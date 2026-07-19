# AGENT.md - JASM Guide for AI Agents

**Last Updated:** 2026-07-18
**Project:** JASM - Custom Features & Stability
**Status:** In Progress

---

## Project Goal

JASM is a custom WinUI 3 skin manager for game mods. The goal is to safely extend features (like batch operations, custom commands, and new games support) while maintaining high codebase stability and avoiding unnecessary refactorings of the core file structure.

---

## Critical Knowledge - READ THESE FIRST

### Source of Truth
**All project tracking is in:** `c:\Users\zurce\Code\JASM\memory\`

**DO NOT use standard tool-specific plan mode files**. Always refer to and update files in the `memory` folder.

### Required Reading (In Order)
1. **[[[Knowledge] JASM.md]]** - Foundational goals, existing codebase analysis, completed tasks, and key decisions.
2. **This file (AGENT.md)** - Key conventions, build workflows, pitfalls, and design rules.
3. **[[Epic Structure]]** - Custom tasks board showing completed, in-progress, and next tasks.
4. **Check `Completed/` folder** for task-level summaries (decision rationale, files changed).

### Development Environment
- **OS**: Windows
- **Terminal Shell**: PowerShell
- **Target Platform**: x64

---

## Architecture - CRITICAL

### MVVM Pattern (CommunityToolkit.Mvvm)
The project uses the standard MVVM design pattern. Pages are declared in XAML with corresponding code-behind and ViewModels.
- **Observable Properties**: Properties in ViewModels are decorated with `[ObservableProperty]` to auto-generate getter/setter notifications.
- **Relay Commands**: Methods are decorated with `[RelayCommand]` to auto-generate `Command` wrappers.

**Reference implementation (DO copy):**
- [CharactersViewModel.cs](file:///c:/Users/zurce/Code/JASM/src/GIMI-ModManager.WinUI/ViewModels/CharactersViewModel.cs) / [CharactersPage.xaml](file:///c:/Users/zurce/Code/JASM/src/GIMI-ModManager.WinUI/Views/CharactersPage.xaml) (Main overview layout, dialog bindings).
- [CommandService.cs](file:///c:/Users/zurce/Code/JASM/src/GIMI-ModManager.Core/Services/CommandService/CommandService.cs) (JSON serialization/deserialization).

---

## Development Workflow

### 1. Verification & Testing

#### Clean & Build
To build the application using the mandatory `x64` platform configuration:
```powershell
dotnet clean src\GIMI-ModManager.WinUI\GIMI-ModManager.WinUI.csproj
dotnet build src\GIMI-ModManager.WinUI\GIMI-ModManager.WinUI.csproj -p:Platform=x64
```

#### Run GUI App Locally
To compile and launch the WinAppSDK desktop interface:
```powershell
dotnet run --project src\GIMI-ModManager.WinUI\GIMI-ModManager.WinUI.csproj -p:Platform=x64
```

### 2. Task Tracking Workflow
For each new feature or bug fix:
1. Document the plan in `Epic Structure.md` and add task lists.
2. Create a task document under `memory/In Progress/[TASK-ID] Title.md` detailing the planned changes.
3. Once completed, move the task document to `memory/Completed/`.
4. Update `Epic Structure.md` and `[Knowledge] JASM.md` to reflect completion.

---

## Project-Specific Preferences

- **Minimal Risk Refactoring**: Never refactor working subsystems unless requested. Follow existing code patterns and keep PR scopes tightly targeted.
- **Powershell Compatibility**: Always use Windows shell command formatting (forward/backward slash escaping when needed) instead of bash commands.

---

## Common Pitfalls

### 1. WinUI 3 ContentDialog XamlRoot Null Reference
**Symptom**: Calling `dialog.ShowAsync()` on ContentDialog instances (both programmatic or XAML-declared) throws an `InvalidOperationException` if `dialog.XamlRoot` is null at runtime.
- **Fix**: Always set the XamlRoot before showing the dialog:
  ```csharp
  dialog.XamlRoot ??= App.MainWindow.Content.XamlRoot;
  ```

### 2. Release Build JSON Trimming
**Symptom**: Config settings or command arguments disappear in Release builds.
- **Fix**: When editing models serialized via `System.Text.Json`, ensure they are covered by `CommandJsonContext.cs` (source generator context) to avoid Reflection-based property trimming during Release optimizations.

---

## Key Reference Files

### MVVM UI & Dialogs:
- [CharactersPage.xaml](file:///c:/Users/zurce/Code/JASM/src/GIMI-ModManager.WinUI/Views/CharactersPage.xaml)
- [CharactersViewModel.cs](file:///c:/Users/zurce/Code/JASM/src/GIMI-ModManager.WinUI/ViewModels/CharactersViewModel.cs)

### Trimming-safe JSON Context:
- [CommandJsonContext.cs](file:///c:/Users/zurce/Code/JASM/src/GIMI-ModManager.Core/Services/CommandService/JsonModels/CommandJsonContext.cs)
- [CommandService.cs](file:///c:/Users/zurce/Code/JASM/src/GIMI-ModManager.Core/Services/CommandService/CommandService.cs)
