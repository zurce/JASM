import re

def get_context(name, value):
    # ── Notification strings ──
    if name.startswith('Notification_'):
        concept = name.replace('Notification_', '').replace('_', ' ')
        if 'Msg' in name or 'Message' in name:
            return f'Notification body message: {value[:60]}... Shown when {concept.lower().replace(" msg", "").replace(" message", "")}'
        if 'Title' in name:
            return f'Notification title/header. Shown when {concept.lower().replace(" title", "")}'
        return f'Notification title or message. Related to: {concept.lower()}'

    # ── Format strings with {0} ──
    if '{0}' in value:
        if 'character' in name.lower():
            return 'The {0} placeholder is replaced with the character name'
        elif 'mod' in name.lower():
            return 'The {0} placeholder is replaced with the mod name or count'
        elif 'folder' in name.lower() or 'path' in name.lower():
            return 'The {0} placeholder is replaced with a file path or folder name'
        elif 'count' in name.lower() or 'number' in name.lower():
            return 'The {0} placeholder is replaced with a number/count'
        elif 'error' in name.lower():
            return 'The {0} placeholder is replaced with the error details'
        elif 'command' in name.lower():
            return 'The {0} placeholder is replaced with the command name'
        elif '{1}' in value:
            return 'Contains multiple dynamic value placeholders'
        else:
            return 'Contains a dynamic value placeholder'

    # ── Dialog buttons ──
    if 'PrimaryButton' in name or 'SecondaryButton' in name:
        return _dialog_button_context(name, value)
    if 'CloseButton' in name and 'Dialog' in name:
        return 'Cancel/dismiss button in a dialog'

    # ── Page-specific contexts ──
    ctx = _page_context(name, value)
    if ctx:
        return ctx

    # ── Property-based fallback ──
    suffix = name.split('.')[-1] if '.' in name else ''
    parts = name.split('_')
    page = parts[0] if parts else 'unknown'
    if suffix == 'Content':
        return f'Button or toggle label on the {page} component'
    elif suffix == 'Text':
        return f'Static label text on the {page} component'
    elif suffix == 'Header':
        return f'Form field label on the {page} component'
    elif suffix == 'Title':
        return f'Section or dialog title on the {page} component'
    elif 'Placeholder' in suffix:
        return f'Hint text inside an input field on the {page} component'
    elif 'ToolTip' in suffix:
        return f'Tooltip explanation on the {page} component'
    elif suffix == 'Message':
        return f'Informational message on the {page} component'
    elif 'NavigateUri' in suffix:
        return f'URL for a hyperlink on the {page} component'
    elif suffix == 'Label':
        return f'Accessibility label for a control on the {page} component'
    elif suffix == 'Description':
        return f'Descriptive text on the {page} component'
    elif suffix == 'OffContent':
        return f'Label when the toggle is in the OFF position on the {page} component'
    elif suffix == 'OnContent':
        return f'Label when the toggle is in the ON position on the {page} component'
    elif suffix == 'PlaceholderText' or suffix == 'PlaceHolderText':
        return f'Hint text inside an input field on the {page} component'
    elif suffix == 'ToolTip' or suffix == 'ToolTipService.ToolTip':
        return f'Tooltip explanation on the {page} component'
    else:
        return f'Text on the {page} component'


def _dialog_button_context(name, value):
    mapping = {
        'DisableAll': '"Disable All Mods" dialog',
        'EnableAll': '"Enable All Mods" dialog',
        'CleanUp': '"Clean Up Disabled Mods" dialog',
        'RestartRequired': '"Restart Required" dialog',
        'SwitchGame': '"Switch Game" dialog',
        'Export': 'mod export dialog',
        'SelectProcessDialog': 'process path selection dialog',
        'CharacterSkins': 'character skins dialog',
        'ReorganizeMods': '"Reorganize Mods" dialog',
        'ClearEmptyFolders': '"Clear Empty Folders" dialog',
        'UpdateFolderPaths': '"Update Folder Paths" dialog',
        'DuplicateModDialog': 'duplicate mod warning dialog',
    }
    for key, ctx in mapping.items():
        if key in name:
            return f'Button label in the {ctx}'
    return 'Button label in a dialog'


def _page_context(name, value):
    # ── Characters page ──
    if name.startswith('CharactersPage_') and 'Dialog' not in name and 'Button' not in name and 'PrimaryButton' not in name:
        if 'SelectProcessDialog' in name:
            if 'Title' in name:
                return 'Title of the dialog for selecting game/3DMigoto executable path'
            if 'Description' in name:
                return 'Explains what the process path selection dialog does'
            if 'GamePlaceholderText' in name:
                return 'Hint text inside the file path input when selecting a game executable'
            if 'ModelImporterPlaceholderText' in name:
                return 'Hint text inside the file path input when selecting a 3DMigoto executable'
            if 'PrimaryButtonText' in name:
                return '"Save" button in the process path selection dialog'
            if 'SecondaryButtonText' in name:
                return '"Cancel" button in the process path selection dialog'
        if 'CancelButton' in name:
            return 'Cancel/close button in a delete mods dialog'
        if 'EnableAllDialog' in name:
            if 'Title' in name:
                return 'Title of the confirmation dialog for enabling all mods'
            if 'Description' in name:
                return 'Explains what happens when enabling all mods'
            return 'Button in the "Enable All Mods" dialog'
        if 'DisableAllDialog' in name:
            if 'Title' in name:
                return 'Title of the confirmation dialog for disabling all mods'
            if 'Description' in name:
                return 'Explains what happens when disabling all mods'
            return 'Button in the "Disable All Mods" dialog'
        if 'CleanUpDialog' in name:
            if 'Title' in name:
                return 'Title of the confirmation dialog for cleaning up disabled mod folders'
            if 'Description' in name:
                return 'Explains what happens during cleanup of disabled mods'
            return 'Button in the "Clean Up" dialog'
        if 'Start3Dmigoto' in name:
            return 'Button label to launch the 3DMigoto model importer process'
        if 'StartGamePrefix' in name:
            return 'Prefix for the Start Game button. The game name is appended (e.g. "Start Genshin Impact")'
        if 'Sort' in name or 'Filters' in name or 'Batch' in name:
            return 'Section label for character filters or batch operations'
        if 'RefreshMods' in name:
            return 'Button label to refresh/scand mod files'
        if 'OpenFolder' in name:
            return 'Button label to open the mods folder in file explorer'
        if 'ConfirmPreset' in name:
            return 'Confirmation text when applying a preset'
        if 'DropHereText' in name:
            return 'Drag-and-drop hint text shown on character cards'
        if 'ApplyPreset' in name:
            return 'Label for preset application status'

    # ── Character Details / CharDetails ──
    if name.startswith('CharacterDetails_') or name.startswith('CharDetails_'):
        if 'Delete' in name:
            if 'Dialog' in name and 'Content' in name:
                return 'Checkbox label: whether to move deleted mods to Recycle Bin'
            if 'Button' in name and 'Text' in name:
                return 'Delete button label in the mod deletion dialog'
            if 'Title' in name:
                return 'Title of the delete mods dialog. {0} is replaced with the count of mods'
            if 'Error' in name:
                return 'Notification title when mod deletion fails'
            if 'Success' in name:
                return 'Notification message when mods were deleted successfully'
            if 'ModsDeleted' in name:
                return 'Notification title when mods are deleted'
            if 'PresetError' in name:
                return 'Notification text: lists mods that had preset errors'
            if 'RemovePresets' in name:
                return 'Checkbox label: remove deleted mods from presets too'
        if 'Move' in name:
            return 'UI for moving mods between characters'
        if 'OpenModFolder' in name or 'OpenFolder' in name:
            return 'Button label to open a mod folder in file explorer'
        if 'RefreshMods' in name:
            return 'Menu item or button to refresh mods'
        if 'SingleSelect' in name:
            return 'Toggle: when enabled, only one mod can be active at a time'
        if 'SavePreferences' in name or 'ReadPreferences' in name:
            return 'Toggle: saves/reads mod preferences (enabled state, order) for this mod'
        if 'ViewToggle' in name:
            return 'Toggle switch to switch between Gallery View or Detailed View'
        if 'RunCommand' in name:
            return 'Button or section for running custom commands on a mod'
        if 'Override' in name:
            return 'UI for managing skin overrides on this character'
        if 'MultipleModsActive' in name:
            return 'Warning message shown when multiple mods are enabled simultaneously'
        if 'Search' in name:
            return 'Placeholder text in the mod search box'
        if 'ShowModFolder' in name:
            return 'Toggle: shows/hides the mod folder name column'
        if 'ModsSelected' in name:
            return 'Shows how many mods are currently selected'
        if 'Loading' in name:
            return 'Loading text'
        if 'ErrorSwitch' in name:
            return 'Notification: error switching character skin'
        if 'ErrorDisab' in name:
            return 'Notification: error disabling mods'
        if 'ErrorEnab' in name:
            return 'Notification: error enabling mods'
        if 'CouldNotDisab' in name:
            return 'Notification body: could not disable all mods'
        if 'ErrorOpen' in name:
            return 'Notification: error opening mod page'
        if 'DragDrop' in name:
            return 'Notification: error during drag-and-drop'
        if 'ErrorDelet' in name:
            return 'Notification: error deleting mods'
        if 'Clipboard' in name:
            return 'Notification: clipboard operation message'
        if 'CannotMoveMods' in name:
            return 'Notification: cannot move mods between characters'
        if 'DestModListNotFound' in name:
            return 'Notification: destination character not found for mod move'
        if 'ErrorLoad' in name:
            return 'Notification: error loading character data'
        if 'OverrideText' in name or 'OverrideSkinSet' in name:
            return 'Text next to overriden skin settings'

    # ── Gallery page ──
    if name.startswith('CharacterGalleryPage_') or name.startswith('CharacterCard_'):
        if 'SelectSkin' in name:
            return 'Menu item to select a character skin'
        if 'EnableButton' in name:
            return 'Button to enable all mods for this character'
        if 'DisableButton' in name:
            return 'Button to disable all mods for this character'
        if 'SingleSelect' in name:
            return 'Toggle/tooltip: restrict to one active mod per character'
        if 'ViewToggleSwitch' in name:
            return 'Toggle switch for Gallery View / Detailed View'
        if 'Sort' in name:
            return 'Sort option label'
        if 'SearchMods' in name:
            return 'Placeholder text in the mod search box'
        if 'DeleteMod' in name or 'SaveImage' in name or 'OpenModFolder' in name or 'OpenModPage' in name:
            return 'Context menu item for mod operations'
        if 'GoToModsOverview' in name:
            return 'Tooltip: navigate to this character in Mods Overview'
        if 'EditCharacter' in name:
            return 'Tooltip: open the character editor for this character'
        if 'DisableAllMods' in name:
            return 'Button title to disable all mods for this character'
        if 'TrackedMods' in name or 'SelectedInGameSkin' in name:
            return 'Displays mod count or selected skin info on the character card'

    # ── Character Manager pages ──
    if name.startswith('CharacterManagerPage_') or name.startswith('CreateCharacterPage_') or \
       name.startswith('EditCharacterPage_') or name.startswith('EditCharacter_') or name.startswith('CreateCharacter_'):
        if 'SearchBox' in name:
            return 'Placeholder or label for the character search box'
        if 'DisplayName' in name:
            return 'Label for the character display name field'
        if 'InternalName' in name:
            return 'Label/help for the internal name (used for mod folder naming, must be unique)'
        if 'IsMultiMod' in name:
            return 'Toggle/help: whether this character supports multiple mods enabled at once'
        if 'SearchKeys' in name:
            return 'Section label or help: additional search keywords for this character'
        if 'Rarity' in name:
            return 'Label for the rarity selector'
        if 'Element' in name:
            return 'Label for the element/type selector'
        if 'ModFilesName' in name:
            return 'Label/help for the mod files folder name'
        if 'SaveButton' in name or 'SaveChanges' in name:
            return 'Button to save character changes'
        if 'DeleteCharacter' in name:
            return 'Button/dialog for deleting a custom character'
        if 'EnableCharacter' in name:
            return 'Button/dialog for enabling a disabled character'
        if 'DisableCharacter' in name:
            return 'Button/dialog for disabling a character'
        if 'ResetCharacter' in name:
            return 'Button/dialog for resetting a character'
        if 'ShowDataModel' in name:
            return 'Button to show the character JSON data model'
        if 'AddSearchKey' in name or 'RemoveKey' in name:
            return 'Button to add or remove search keywords'
        if 'ExportJson' in name:
            return 'Button to export character data as JSON'
        if 'OpenCustomJson' in name:
            return 'Button to import a custom JSON file'
        if 'GoToCharacter' in name:
            return 'Button to navigate to this character'
        if 'UndoChanges' in name:
            return 'Button to revert unsaved edits'
        if 'NoImage' in name:
            return 'Notification: clipboard has no image'
        if 'PasteImage' in name:
            return 'Notification: paste image operation result'
        if 'JsonExport' in name:
            return 'Dialog/notification for JSON export'
        if 'EnableFailed' in name or 'FailedTitle' in name or 'SaveFailed' in name:
            return 'Notification: an operation failed'
        if 'Success' in name or 'Copied' in name:
            return 'Notification: operation succeeded'

    # ── Mod Installer ──
    if name.startswith('ModInstallerPage_') or name.startswith('ModInstaller_'):
        if 'AddModButton' in name:
            return 'Button to finish installing the mod'
        if 'FolderName' in name:
            return 'Label for the mod folder name input'
        if 'CustomName' in name:
            return 'Label for the custom display name input'
        if 'Label' in name:
            return 'Section heading text'
        if 'Author' in name:
            return 'Label for the mod author field'
        if 'ModPageUrl' in name:
            return 'Label/placeholder for the GameBanana mod page URL'
        if 'Note' in name:
            return 'Label/placeholder for a free-text note about this mod'
        if 'ImageSource' in name:
            return 'Radio button: preview image source (Auto/URL/ModFiles/Ignore)'
        if 'ShaderFixes' in name:
            return 'Radio button: shader fixes folder mode (Auto/Ignore/Manual)'
        if 'OverwriteExisting' in name:
            return 'Checkbox: overwrite if a mod with this name already exists'
        if 'EnableOnlyMod' in name:
            return 'Checkbox: disable all other mods, only enable this one'
        if 'ReplaceInPresets' in name or 'ReplaceDuplicateMod' in name:
            return 'Checkbox: replace old mod in presets with this new version'
        if 'DuplicateModDialog' in name:
            return 'Warning dialog about already-installed mod'
        if 'RetrieveModInfoButton' in name:
            return 'Button/tooltip: fetch mod info from GameBanana'
        if 'AlwaysOnTopToggle' in name:
            return 'Toggle tooltip: keep window always on top'
        if 'PreviewImage' in name or 'SetModPreviewImage' in name:
            return 'UI for setting a custom preview image'
        if 'SetRootModFolder' in name or 'SetShaderFixesFolder' in name:
            return 'Button to select a folder path'
        if 'ForceOverwriteDifferentNameMod' in name:
            return 'Checkbox: overwrite old mod even if folder names differ'
        if 'ManualImage' in name:
            return 'Instruction for manual image selection'
        if 'HelperWindowTitle' in name:
            return 'Title of the helper/side window'
        if 'AddedToModList' in name:
            return 'Notification: mod added to character mod list'
        if 'ModInstalled' in name:
            return 'Notification: mod installed successfully'
        if 'AnErrorOccurred' in name or 'ErrorAdding' in name:
            return 'Notification: mod installation error'
        if 'FailedDownload' in name:
            return 'Notification: failed to download preview image'

    # ── Presets ──
    if name.startswith('PresetPage_') and 'Dialog' not in name:
        if 'RandomizeMods' in name:
            return 'Button to open the randomize mods dialog'
        if 'Apply' in name or 'Confirm' in name:
            return 'Button to apply or confirm a preset'
        if 'Create' in name:
            return 'Button to create a new preset'
        if 'Delete' in name:
            return 'Context menu: delete preset'
        if 'Duplicate' in name:
            return 'Context menu: duplicate preset'
        if 'Rename' in name:
            return 'Button/dialog for renaming a preset'
        if 'Reset' in name:
            return 'Button to reset mod preferences'
        if 'SaveActive' in name:
            return 'Button to save current mod states as preferences'
        if 'AutoSync' in name:
            return 'Toggle: auto-sync mod preferences'
        if 'ReadOnly' in name:
            return 'Toggle: mark preset as read-only'
        if 'NewPresetName' in name:
            return 'Placeholder/label for new preset name'
        if 'HowPresetsWork' in name:
            return 'Dialog explaining how presets work'
        if 'ShowManual' in name:
            return 'Button to show manual controls'
        if 'AlsoReset3DMigoto' in name:
            return 'Checkbox: also reset 3DMigoto config when resetting'

    if name.startswith('PresetDetailsPage_'):
        if 'SearchBox' in name:
            return 'Placeholder text in mod search box'
        if 'AddMod' in name:
            return 'Button to add a mod to this preset'
        if 'ModNotFound' in name:
            return 'Warning: mod in preset not found'
        if 'RemoveModFromPreset' in name:
            return 'Button to remove mod from preset'
        if 'ReadAndSavePreferences' in name:
            return 'Description text for the read/save preferences UI'
        if 'FindReplacement' in name:
            return 'Button to find a replacement for a missing mod'

    if name.startswith('Preset_') or name.startswith('PresetDetails_'):
        if 'Failed' in name or 'Error' in name:
            return 'Notification: preset operation failed'
        if 'Applied' in name or 'Added' in name or 'Removed' in name:
            return 'Notification: preset operation succeeded'
        if 'ModPrefs' in name:
            return 'Notification: mod preferences updated'

    # ── Commands ──
    if name.startswith('CreateCommandView_'):
        if 'CommandName' in name:
            return 'Label/placeholder for the command display name (Required field)'
        if 'Executable' in name:
            return 'Label/placeholder/title for the executable path (Required field)'
        if 'WorkingDirectory' in name:
            return 'Label/placeholder for the working directory'
        if 'Arguments' in name:
            return 'Label/placeholder for command arguments'
        if 'Title' in name:
            return 'Window title for the command creator'
        if 'SaveCommand' in name:
            return 'Button to save the command definition'
        if 'Browse' in name:
            return 'Button to browse for a file'
        if 'RunAsAdmin' in name:
            return 'Checkbox: launch command with admin privileges'
        if 'UseShellExecute' in name:
            return 'Checkbox: use OS shell to launch the command'
        if 'CreateWindow' in name:
            return 'Checkbox: show a visible window for the process'
        if 'CustomCommand' in name:
            return 'Radio button: create a fully custom command'
        if 'ChainedCommand' in name:
            return 'Radio button: use a game start command as base'
        if 'KillProcess' in name:
            return 'Checkbox: kill the process when JASM exits'
        if 'CommandPreview' in name:
            return 'Label showing the full command string'
        if 'TargetPathHelp' in name:
            return 'Help text explaining the {{TargetPath}} variable'

    if name.startswith('CommandsSettingsPage_'):
        if 'Title' in name:
            return 'Page title for the command settings page'
        if 'CreateCommand' in name or 'CreateNewCommand' in name:
            return 'Button/section to create a new command'
        if 'RunButton' in name or 'KillButton' in name:
            return 'Button to run or kill a command'
        if 'EditButton' in name or 'DeleteButton' in name:
            return 'Button to edit or delete a command definition'
        if 'DisplayName' in name or 'CommandDefinitionId' in name or 'FullCommand' in name or 'WorkingDirectory' in name or 'Executable' in name or 'Arguments' in name:
            return 'Label showing command details'
        if 'RunningCommands' in name or 'CommandDefinitions' in name:
            return 'Section header'
        if 'Warning' in name:
            return 'Text in the warning/confirmation dialog'
        if 'RunConfirm' in name or 'DeleteConfirm' in name:
            return 'Text in the confirmation dialog'

    if name.startswith('CreateCommand_') or name.startswith('Commands_'):
        if 'Notification' in name:
            return 'Notification about command creation/update/deletion'
        if 'FilePicker' in name or 'FolderPicker' in name:
            return 'Text for the file/folder picker dialog'

    # ── Mods Overview ──
    if name.startswith('ModsOverviewPage_'):
        if 'Search' in name:
            return 'Placeholder text in the mod search box'
        if 'RunButton' in name:
            return 'Button to run a command on this mod folder'
        if 'CloseAll' in name:
            return 'Button to close all expanded mod panels'
        if 'GoToCharacter' in name:
            return 'Button to navigate to this character'
        if 'TargetPath' in name or 'WorkingDirectory' in name or 'FullCommand' in name:
            return 'Label showing command path/directory details'
        if 'AddedLabel' in name:
            return 'Label showing when the mod was added'

    # ── Shell/Navigation ──
    if name.startswith('ShellPage_') or name.startswith('ShellMenuItem_') or name.startswith('ShellMenuBar'):
        if 'NavItem' in name or 'MenuItem' in name or 'Item' in name:
            page = name.split('_')[-1]
            return f'Navigation menu item label. Takes user to the {page} page'
        return 'UI element in the main navigation shell'
    if name.startswith('Shell_'):
        return 'UI text in the shell/navigation'

    # ── Error Window ──
    if name.startswith('ErrorWindow_'):
        if 'StackTrace' in name:
            return 'Label for the stack trace section in the error window'
        if 'ErrorHeaderText' in name:
            return 'Main heading in the error window'
        if 'ExceptionDescription' in name:
            return 'Description text explaining the error'
        if 'InnerException' in name:
            return 'Label for the inner exception stack trace'

    # ── Notifications page ──
    if name.startswith('NotificationsPage_'):
        if 'HeaderText' in name:
            return 'Page title for the notifications page'
        if 'LogPath' in name:
            return 'Label showing the log file path'

    # ── Startup page ──
    if name.startswith('Startup'):
        if name in ('Startup1.Text', 'Startup2.Text', 'Startup3.Text'):
            return 'Informational text on the first-time setup page (preserved for reference, may not be in use)'
        return 'UI text on the first-time setup page'

    # ── Debug page ──
    if name.startswith('DebugPage_'):
        return 'Button label on the Debug page'

    # ── GbModPageWindow / ModUpdateAvailableWindow ──
    if name.startswith('GbModPageWindow_') or name.startswith('ModUpdateAvailableWindow_'):
        if 'Close' in name:
            return 'Button to close the window'
        if 'Download' in name:
            return 'Button to download the mod file'
        if 'Install' in name:
            return 'Button to install the mod'
        if 'LoadingText' in name:
            return 'Loading indicator text'
        if 'ModPageLabel' in name:
            return 'Label showing the mod page URL'
        if 'Title' in name:
            return 'Window title'
        if 'IgnoreClose' in name:
            return 'Button to ignore and close'
        if 'LastCheck' in name or 'NewSinceLastCheck' in name:
            return 'Text showing update check time'

    # ── ModGrid / ModPane ──
    if name.startswith('ModGrid_'):
        col = name.split('_', 1)[1] if '_' in name else name
        return f'Column header in the mod list: {col}'
    if name.startswith('ModPane_'):
        if 'ModUrl' in name:
            return 'Label for the mod page URL field'
        if 'ForwardKey' in name or 'BackwardKey' in name:
            return 'Label for key swap configuration fields'
        if 'IgnoreIni' in name or 'SetModIni' in name:
            return 'Button/checkbox for managing the mod .ini file'
        if 'OpenModFolder' in name:
            return 'Button to open the mod folder'
        if 'SaveButtonText' in name:
            return 'Button to save mod settings'
        if 'ModEnabled' in name:
            return 'Toggle: enable/disable this mod'
        if 'KeySwapDisabled' in name:
            return 'Label: key swaps disabled for this mod'
        if 'Variations' in name:
            return 'Section for mod variation options'
        if 'ChangeDisplayName' in name:
            return 'Tooltip: edit the mod display name'
        if 'Unknown' in name:
            return 'Fallback text when mod value is unknown'

    # ── Mod Selector / List ──
    if name.startswith('ModSelector_'):
        if 'SearchBox' in name:
            return 'Placeholder text in the search box'
        if 'SelectModButton' in name:
            return 'Button to confirm mod selection'
        if 'Title' in name:
            return 'Title of the mod selector window'
    if name.startswith('ModListOverview_'):
        if 'ModNameHeader' in name:
            return 'Table column header for mod name'
        if 'ModEnabledHeader' in name:
            return 'Table column header for enabled/disabled status'
        if 'MultipleSkinWarning' in name:
            return 'Warning: multiple mods enabled for this character'
        if 'AddMod' in name:
            return 'Button to add a mod'

    # ── Settings page ──
    if name.startswith('SettingsPage_'):
        if 'Description' in name:
            return 'Description text on the settings page'
        if 'PrivacyTerms' in name and 'Content' in name:
            return 'Privacy statement hyperlink text'
        if 'PrivacyTerms' in name and 'NavigateUri' in name:
            return 'URL for the privacy statement'
    if name.startswith('Settings_') and '_Content' not in name and '_Title' not in name and 'Dialog' not in name:
        if 'SectionHeader' in name:
            return 'Section heading in Settings page'
        if 'Header' in name:
            return 'Form field label in Settings page'
        if 'Title' in name and 'Text' in name:
            return 'Title label in Settings page'
        if 'Content' in name:
            return 'Button/toggle label in Settings page'
        if 'FolderSelector' in name or 'FolderPath' in name:
            return 'Folder path selector label in Settings page'
        if 'GameSource' in name:
            return 'Game source selection UI (Release vs Community)'
        if 'ModUpdateChecker' in name:
            return 'Mod update checker setting UI'
        if 'ComboBox' in name:
            return 'Dropdown selector label in Settings'
        if 'Language' in name:
            return 'Language selection UI in Settings'
    if name.startswith('Settings_') and ('_Title' in name or '_Content' in name):
        return 'Text in a settings confirmation dialog'

    # ── Category labels ──
    if name.startswith('Category_'):
        cat = name.split('_', 1)[1]
        return f'Filter category: {cat}'

    # ── RandomizeModsDialog ──
    if name.startswith('RandomizeModsDialog_'):
        if 'Title' in name:
            return 'Title of the randomize mods dialog'
        if 'Close' in name:
            return 'Close/cancel button'
        if 'Primary' in name:
            return 'Confirm button'
        if 'Desc' in name or 'Suggestion' in name:
            return 'Description/suggestion text'
        if 'AllowNone' in name:
            return 'Toggle: allow all mods to be disabled'
        if 'Notif' in name:
            return 'Notification about randomization'

    # ── WindowManager ──
    if name == 'WindowManagerService_Close':
        return 'Close button text for full-screen dialogs'

    # ── Error / Running / Stopped / Waiting / Overview ──
    if name in ('Error', 'Running', 'Stopped', 'Waiting', 'Overview'):
        return f'Process status indicator: {value}'

    # ── AppDisplayName / AppDescription ──
    if name == 'AppDisplayName':
        return 'Application display name in the window title bar'
    if name == 'AppDescription':
        return 'Application description'

    # ── Other known patterns ──
    if name.startswith('CustomImage_'):
        action = name.split('_', 1)[1].split('.')[0]
        return f'Context menu item to {action.lower()} an image'
    if name.startswith('FolderSelector_'):
        return 'Button in the folder picker control'
    if name.startswith('LinkButton_'):
        return 'Hyperlink button label'
    if name.startswith('Main_Title'):
        return 'Page title for the main view'

    # ── Final fallback ──
    return None


def process_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    pattern = re.compile(
        r'(<data name=\"([^\"]+)\" xml:space=\"preserve\">)\s*(?:<!--[^>]*-->\s*)?<value>([^<]*)</value>',
        re.DOTALL
    )

    def add_comment(match):
        full = match.group(0)
        name = match.group(2)
        value = match.group(3).strip()
        if '<!--' in full:
            return full
        ctx = get_context(name, value)
        if ctx is None:
            # Generic fallback based on property suffix
            suffix = name.split('.')[-1] if '.' in name else ''
            parts = name.split('_')
            page = parts[0] if parts else 'unknown'
            if suffix == 'Content':
                ctx = f'Button or toggle label on the {page} component'
            elif suffix == 'Text':
                ctx = f'Static label text on the {page} component'
            elif suffix == 'Header':
                ctx = f'Form field label on the {page} component'
            elif suffix == 'Title':
                ctx = f'Section or dialog title on the {page} component'
            elif 'Placeholder' in suffix:
                ctx = f'Hint text inside an input field on the {page} component'
            elif 'ToolTip' in suffix:
                ctx = f'Tooltip explanation on the {page} component'
            elif suffix == 'Message':
                ctx = f'Informational message on the {page} component'
            elif suffix == 'Description':
                ctx = f'Descriptive text on the {page} component'
            elif suffix == 'Label':
                ctx = f'Accessibility label on the {page} component'
            elif 'NavigateUri' in suffix:
                ctx = f'URL for a hyperlink on the {page} component'
            elif suffix == 'OffContent':
                ctx = f'Label when toggle is OFF on the {page} component'
            elif suffix == 'OnContent':
                ctx = f'Label when toggle is ON on the {page} component'
            elif suffix == 'PlaceholderText' or suffix == 'PlaceHolderText':
                ctx = f'Hint text inside an input field on the {page} component'
            elif suffix == 'ToolTip' or suffix == 'ToolTipService.ToolTip':
                ctx = f'Tooltip explanation on the {page} component'
            else:
                ctx = f'Text on the {page} component'

        tag_end = match.group(1)
        return f'{tag_end}\n    <!-- {ctx} -->\n    <value>{match.group(3)}</value>'

    updated = pattern.sub(add_comment, content)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(updated)

    added = updated.count('<!--')
    print(f'Processed {filepath}: {added} comments added')
    return added


# Process both files
process_file('en-us/Resources.resw')
process_file('es/Resources.resw')
