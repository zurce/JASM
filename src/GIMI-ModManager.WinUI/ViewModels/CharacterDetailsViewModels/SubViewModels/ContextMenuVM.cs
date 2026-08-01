using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GIMI_ModManager.Core.Contracts.Entities;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Entities;
using GIMI_ModManager.Core.Entities.Mods.Contract;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.GamesService.Interfaces;
using GIMI_ModManager.Core.GamesService.Models;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.WinUI.Helpers;
using GIMI_ModManager.WinUI.Models.CustomControlTemplates;
using GIMI_ModManager.WinUI.Services.ModHandling;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.Core.Services.CommandService;
using GIMI_ModManager.Core.Services.CommandService.Models;
using GIMI_ModManager.WinUI.Services;
using Microsoft.UI.Dispatching;
using Serilog;

namespace GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;

public partial class ContextMenuVM(
    ISkinManagerService skinManagerService,
    IGameService gameService,
    NotificationManager notificationManager,
    ILogger logger,
    ModSettingsService modSettingsService,
    CommandHandlerService commandHandlerService)
    : ObservableRecipient
{
    private readonly ISkinManagerService _skinManagerService = skinManagerService;
    private readonly IGameService _gameService = gameService;
    private readonly NotificationManager _notificationManager = notificationManager;
    private readonly ILogger _logger = logger.ForContext<ContextMenuVM>();
    private readonly ModSettingsService _modSettingsService = modSettingsService;
    private readonly CommandHandlerService _commandHandlerService = commandHandlerService;

    private DispatcherQueue _dispatcherQueue = null!;
    private CancellationToken _navigationCt = default;
    private ICharacterModList _modList = null!;
    private ModDetailsPageContext _context = null!;
    private BusySetter _busySetter = null!;

    private List<Guid> _selectedMods = [];

    [ObservableProperty] private int _selectedModsCount;
    [ObservableProperty] private bool _isSingleModSelected;
    [ObservableProperty] private bool _isCharacter;
    [ObservableProperty] private bool _multipleSkins;

    public ObservableCollection<SuggestedModObject> SuggestedModdableObjects { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveModsCommand))]
    private SuggestedModObject? _selectedSuggestedModObject;

    [ObservableProperty] private string _moveModsSearchText = string.Empty;


    public ObservableCollection<SelectCharacterTemplate> SelectableCharacterSkins { get; init; } = new();
    [ObservableProperty] private bool _modHasCharacterSkinOverride;

    [ObservableProperty] private SelectedSkinVm? _modCharacterSkinOverride;

    public ObservableCollection<ModOverviewCommandVM> CommandDefinitions { get; } = new();

    public Task InitializeAsync(ModDetailsPageContext context, BusySetter busySetter, CancellationToken navigationCt)

    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _navigationCt = navigationCt;
        ChangeSkin(context);
        _busySetter = busySetter;

        _modList = _skinManagerService.GetCharacterModList(context.ShownModObject);

        _ = LoadCommandsAsync();

        return Task.CompletedTask;
    }

    public void ChangeSkin(ModDetailsPageContext context)
    {
        _context = context;
        IsCharacter = context.IsCharacter;
        SelectableCharacterSkins.Clear();
        if (!context.IsCharacter) return;

        MultipleSkins = context.Skins.Count > 1;

        foreach (var characterSkin in context.Skins)
        {
            var template = new SelectCharacterTemplate(characterSkin);
            template.IsSelected = characterSkin == context.SelectedSkin;
            SelectableCharacterSkins.Add(template);
        }
    }

    public bool CanOpenContextMenu => SelectedModsCount > 0 && _busySetter.IsNotHardBusy;

    public void SetSelectedMods(IEnumerable<Guid> selectedMods)
    {
        _selectedMods = selectedMods.ToList();
        SelectedModsCount = _selectedMods.Count;
        IsSingleModSelected = SelectedModsCount == 1;
        MoveModsCommand.NotifyCanExecuteChanged();

        if (!IsSingleModSelected)
        {
            foreach (var commandDefinition in CommandDefinitions)
            {
                commandDefinition.TargetPath = string.Empty;
            }
            return;
        }

        var mod = _modList.Mods.FirstOrDefault(m => m.Id == _selectedMods[0]);
        if (mod is null) return;

        var targetPath = mod.Mod.FullPath;
        foreach (var commandDefinition in CommandDefinitions)
        {
            commandDefinition.TargetPath = targetPath;
        }

        if (!mod.Mod.Settings.TryGetSettings(out var modSettings)) return;

        var skinOverride = ResolveSkinOverride(mod, modSettings);

        ModHasCharacterSkinOverride = skinOverride is not null;
        ModCharacterSkinOverride = skinOverride is not null ? new SelectedSkinVm(skinOverride) : null;
        SelectNewCharacterSkinCommand.NotifyCanExecuteChanged();
        OverrideModCharacterSkinCommand.NotifyCanExecuteChanged();
    }

    private ICharacterSkin? ResolveSkinOverride(CharacterSkinEntry skinEntry, ModSettings settings)
    {
        if (settings.CharacterSkinOverride.IsNullOrEmpty() || !_context.IsCharacter)
            return null;

        // Check selected characters skins first
        var selectedSkin = _context.Skins.FirstOrDefault(s => s.InternalNameEquals(settings.CharacterSkinOverride));
        if (selectedSkin != null)
            return selectedSkin;

        // Check all skins
        var allSkins = _gameService.GetCharacters();
        selectedSkin = allSkins.SelectMany(c => c.Skins).FirstOrDefault(s => s.InternalNameEquals(settings.CharacterSkinOverride));

        return selectedSkin;
    }

    private List<CharacterSkinEntry> ResolveSelectedMods() => _modList.Mods.Where(m => _selectedMods.Contains(m.Id)).ToList();


    #region Commands

    private bool CanMoveMods() => SelectedModsCount > 0
                                  && !_busySetter.IsWorking
                                  && SelectedSuggestedModObject is not null;

    [RelayCommand(CanExecute = nameof(CanMoveMods))]
    private async Task MoveModsAsync()
    {
        using var busy = _busySetter.StartHardBusy();
        var destinationModList = _skinManagerService.GetCharacterModListOrDefault(SelectedSuggestedModObject?.InternalName ?? "");


        if (destinationModList is null)
        {
            _logger.Warning(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("CharDetails_DestModListNotFoundMsg") ?? "Destination mod list not found");
            _notificationManager.ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("CharDetails_DestModListNotFound") ?? "Destination Mod List Not Found",
                App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("CharDetails_DestModListNotFoundMsg") ?? "Destination mod list not found", TimeSpan.FromSeconds(5));
            return;
        }

        var selectedMods = ResolveSelectedMods();

        try
        {
            await Task.Run(() => _skinManagerService.TransferMods(_modList, destinationModList,
                selectedMods.Select(modEntry => modEntry.Id)));
        }
        catch (InvalidOperationException e)
        {
            _logger.Error(e, "Error moving mods");
            _notificationManager
                .ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("CharDetails_InvalidOpException") ?? "Invalid Operation Exception",
                    string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("CharDetails_CannotMoveModsMsg") ?? "Cannot move mods\n{0}, see logs for details.", e.Message), TimeSpan.FromSeconds(10));
            return;
        }

        _notificationManager.ShowNotification(string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("CharDetails_ModsMoved") ?? "{0} Mods Moved", SelectedModsCount),
            string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("CharDetails_MovedSuccessMsg") ?? "Successfully moved {0} mods to {1}", string.Join(",", selectedMods.Select(m => m.Mod.GetDisplayName())), destinationModList.Character.DisplayName),
            TimeSpan.FromSeconds(5));

        ModsMoved?.Invoke(this, EventArgs.Empty);
    }

    private bool CanSelectNewCharacterSkin(SelectCharacterTemplate? characterTemplate) =>
        SelectedModsCount == 1 && characterTemplate != null && _context.IsCharacter;

    [RelayCommand(CanExecute = nameof(CanSelectNewCharacterSkin))]
    private void SelectNewCharacterSkin(SelectCharacterTemplate? characterTemplate)
    {
        if (characterTemplate == null || !_context.IsCharacter) return;

        var characterSkinToSet = _context.Character.Skins.FirstOrDefault(charSkin =>
            charSkin.InternalName.Equals(characterTemplate.InternalName));

        if (characterSkinToSet == null)
            return;

        foreach (var selectableCharacterSkin in SelectableCharacterSkins) selectableCharacterSkin.IsSelected = false;
        characterTemplate.IsSelected = true;
        OverrideModCharacterSkinCommand.NotifyCanExecuteChanged();
    }

    private bool CanOverrideModCharacterSkin()
    {
        var selectedTemplate = SelectableCharacterSkins.Where(c => c.IsSelected).ToArray();
        if (selectedTemplate.Length != 1) return false;
        var selectedCharacterSkin = selectedTemplate[0];

        if (!_context.IsCharacter) return false;

        var isDifferentSkinSelected =
                // If mod already has an override
                (ModCharacterSkinOverride is not null &&
                 !ModCharacterSkinOverride.InternalName.Equals(selectedCharacterSkin.InternalName, StringComparison.OrdinalIgnoreCase)) ||
                // If mod has no override
                ModCharacterSkinOverride is null && !_context.SelectedSkin.InternalNameEquals(selectedCharacterSkin.InternalName)
            ;

        return _busySetter.IsNotHardBusy && SelectableCharacterSkins.Any(c => c.IsSelected) && SelectedModsCount == 1 && isDifferentSkinSelected;
    }

    [RelayCommand(CanExecute = nameof(CanOverrideModCharacterSkin))]
    private async Task OverrideModCharacterSkin()
    {
        var selectedCharacterSkin = SelectableCharacterSkins.FirstOrDefault(c => c.IsSelected);
        if (selectedCharacterSkin == null || !_context.IsCharacter) return;

        var character = _context.Character;

        var characterSkinToSet = character.Skins.FirstOrDefault(charSkin => charSkin.InternalNameEquals(selectedCharacterSkin.InternalName));

        if (characterSkinToSet == null)
        {
            Debugger.Break(); // Should not happen
            return;
        }

        var modEntry = _modList.Mods.FirstOrDefault(m => m.Id == _selectedMods[0]);
        if (modEntry is null) return;


        var result = await _modSettingsService.SetCharacterSkinOverrideLegacy(modEntry.Id, characterSkinToSet.InternalName);


        if (result.IsT0)
        {
            _notificationManager.ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("CharDetails_SkinOverrideChanged") ?? "Changed skin override for mod",
                string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("CharDetails_SkinOverrideSetMsg") ?? "Set skin override for mod '{0}' to {1}", modEntry.Mod.GetDisplayName(), characterSkinToSet.DisplayName), null);
        }
        else
        {
            var error = result.IsT1 ? result.AsT1.ToString() : result.AsT2.ToString();
            _logger.Error("Failed to override character skin for mod {modName}", modEntry.Mod.GetDisplayName());
            _notificationManager.ShowNotification(
                string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("Notification_SkinOverrideFailed") ?? "Failed to override character skin for mod {0}", modEntry.Mod.GetDisplayName()),
                string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("Notification_SkinOverrideErrorMsg") ?? "An Error Occurred. Reason: {0}", error),
                TimeSpan.FromSeconds(5));
        }


        ModCharactersSkinOverriden?.Invoke(this, EventArgs.Empty);
        CloseFlyout?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadCommandsAsync()
    {
        var commandDefinitions =
            await _commandHandlerService.GetCommandsThatContainSpecialVariablesAsync(SpecialVariables.TargetPath);

        _dispatcherQueue.TryEnqueue(() =>
        {
            CommandDefinitions.Clear();

            foreach (var commandDefinition in commandDefinitions)
            {
                CommandDefinitions.Add(new ModOverviewCommandVM(commandDefinition)
                {
                    RunCommand = RunCommandCommand,
                    OpenFolder = OpenFolderCommand
                });
            }
        });
    }

    [RelayCommand]
    private async Task RunCommandAsync(ModOverviewCommandVM? commandDefinitionVM)
    {
        if (commandDefinitionVM is null || string.IsNullOrEmpty(commandDefinitionVM.TargetPath))
            return;

        var result = await Task.Run(async () => await _commandHandlerService.RunCommandAsync(commandDefinitionVM.Id,
            SpecialVariablesInput.CreateWithTargetPath(commandDefinitionVM.TargetPath)).ConfigureAwait(false));

        if (result.HasNotification)
            _notificationManager.ShowNotification(result.Notification);

        CloseFlyout?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task OpenFolderAsync(ModOverviewCommandVM? commandDefinitionVM)
    {
        if (commandDefinitionVM is null || string.IsNullOrEmpty(commandDefinitionVM.TargetPath) || !Directory.Exists(commandDefinitionVM.TargetPath))
            return;
        await Windows.System.Launcher.LaunchFolderPathAsync(commandDefinitionVM.TargetPath);
    }

    #endregion


    #region Events

    public event EventHandler? ModsMoved;
    public event EventHandler? ModCharactersSkinOverriden;
    public event EventHandler? CloseFlyout;

    #endregion


    #region EventHandlers

    /// <summary>
    /// Requests that the mod-row context-menu flyout be closed.
    /// Called from outside (e.g. the parent view-model) when a flyout-buttond action
    /// opens a ContentDialog and the still-open flyout would overlap it.
    /// </summary>
    public void RequestCloseFlyout() => CloseFlyout?.Invoke(this, EventArgs.Empty);

    public void OnFlyoutClosing()
    {
        SuggestedModdableObjects.Clear();
        SelectedSuggestedModObject = null;
        MoveModsSearchText = string.Empty;
        ModHasCharacterSkinOverride = false;
        ModCharacterSkinOverride = null;
    }

    public void SearchTextChanged(string senderText)
    {
        SuggestedModdableObjects.Clear();
        SelectedSuggestedModObject = null;
        if (string.IsNullOrWhiteSpace(senderText))
        {
            return;
        }

        var result = _gameService.QueryModdableObjects(senderText, minScore: 120)
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(x => new SuggestedModObject(x.Key));

        SuggestedModdableObjects.AddRange(result);
    }

    public void OnSuggestionChosen(SuggestedModObject? modObject)
    {
        if (modObject is null)
            return;

        SelectedSuggestedModObject = modObject;
        SuggestedModdableObjects.Clear();
    }

    #endregion
}

public sealed class SuggestedModObject(IModdableObject moddableObject)
{
    public InternalName InternalName { get; } = moddableObject.InternalName;
    public string DisplayName { get; } = moddableObject.DisplayName;

    public override string ToString() => DisplayName;
}

public sealed class SelectedSkinVm(ICharacterSkin skin)
{
    public string DisplayName => skin.DisplayName;
    public string InternalName => skin.InternalName;
}