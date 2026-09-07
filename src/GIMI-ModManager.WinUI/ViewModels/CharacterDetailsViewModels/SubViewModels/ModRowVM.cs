using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GIMI_ModManager.Core.Entities;
using GIMI_ModManager.Core.Entities.Mods.Contract;
using GIMI_ModManager.WinUI.Helpers;
using GIMI_ModManager.WinUI.Helpers.Xaml;
using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Xaml;
using GIMI_ModManager.WinUI.Services.Notifications;

namespace GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;

public partial class ModRowVM : ObservableObject
{
    private ModRowVM()
    {
    }

    public Guid Id { get; init; }
    [ObservableProperty] private bool _isSelected;

    [ObservableProperty] private bool _isEnabled;

    [ObservableProperty] private string _displayName = string.Empty;

    [ObservableProperty] private string _folderName = string.Empty;

    [ObservableProperty] private string _absFolderPath = string.Empty;

    [ObservableProperty] private DateTime _dateAdded;

    [ObservableProperty] private string _dateAddedFormated = string.Empty;

    [ObservableProperty] private string _author = string.Empty;
    [ObservableProperty] private string[] _presets = [];
    [ObservableProperty] private string _inPresets = string.Empty;

    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _notesPreview = string.Empty;


    public ObservableCollection<ModRowVM_ModNotificationVM> ModNotifications { get; } = new();


    public ModRowVM(CharacterSkinEntry characterSkinEntry, ModSettings? modSettings, IEnumerable<string> presetNames,
        IEnumerable<ModNotification> modNotifications)
    {
        Id = characterSkinEntry.Mod.Id;
        UpdateModel(characterSkinEntry, modSettings, presetNames, modNotifications);
    }

    public void UpdateModel(CharacterSkinEntry characterSkinEntry, ModSettings? modSettings,
        IEnumerable<string> presetNames, IEnumerable<ModNotification> modNotifications)
    {
        IsEnabled = characterSkinEntry.IsEnabled;
        DisplayName = characterSkinEntry.Mod.GetDisplayName();
        FolderName = characterSkinEntry.Mod.Name;
        AbsFolderPath = characterSkinEntry.Mod.FullPath;
        DateAdded = modSettings?.DateAdded ?? DateTime.MinValue;
        DateAddedFormated = DateAdded.ToString("d");
        Author = modSettings?.Author ?? string.Empty;
        NotesPreview = modSettings is null || string.IsNullOrWhiteSpace(modSettings.Description)
            ? string.Empty
            : HtmlToRichText.StripHtml(modSettings.Description);
        Description = modSettings?.Description ?? string.Empty;
        Presets = presetNames.ToArray();
        InPresets = string.Join(',', Presets);
        ModNotifications.Clear();
        ModNotifications.AddRange(modNotifications.Select(m => new ModRowVM_ModNotificationVM(m)));


        SearchableText = $"{DisplayName}{FolderName}{Author}{string.Join(null, Presets)}{DateAdded:d}{Description}";
    }

    // Add-on rows (dependent mods nested inside a parent mod folder). They are built from
    // the parent's addons[] config, not from tracking, and carry deterministic ids so
    // selection survives grid rebuilds.
    public bool IsAddon { get; private set; }
    public Guid? ParentModId { get; private set; }
    public bool HasAddons { get; set; }
    public bool IsCollapsed { get; set; }
    public string CollapseGlyph => IsCollapsed ? "\uE76C" : "\uE70D";
    public HorizontalAlignment CheckAlignment =>
        IsAddon ? HorizontalAlignment.Right : HorizontalAlignment.Center;
    public Visibility CollapseChevronVisibility =>
        HasAddons ? Visibility.Visible : Visibility.Collapsed;
    public bool ParentIsEnabled { get; set; } = true;
    public double NameOpacity => !IsAddon || ParentIsEnabled ? 1.0 : 0.45;
    public Action<ModRowVM>? ToggleCollapseAction { get; set; }
    public IRelayCommand ToggleCollapseCommand => new RelayCommand(() => ToggleCollapseAction?.Invoke(this));

    public void NotifyAddonVisualChanged()
    {
        OnPropertyChanged(nameof(IsCollapsed));
        OnPropertyChanged(nameof(CollapseGlyph));
        OnPropertyChanged(nameof(ParentIsEnabled));
        OnPropertyChanged(nameof(NameOpacity));
        OnPropertyChanged(nameof(HasAddons));
        OnPropertyChanged(nameof(CollapseChevronVisibility));
        OnPropertyChanged(nameof(CheckAlignment));
    }

    internal static ModRowVM CreateAddonRow(Guid parentModId, string parentName, ModAddon addon,
        DateTime parentDateAdded, IAsyncRelayCommand toggleEnabledCommand,
        IAsyncRelayCommand updateModSettingsCommand)
    {
        return new ModRowVM
        {
            Id = AddonRowId(parentModId, addon.FolderName),
            IsAddon = true,
            ParentModId = parentModId,
            DisplayName = addon.Name,
            FolderName = addon.FolderName,
            IsEnabled = addon.Enabled,
            DateAdded = parentDateAdded,
            DateAddedFormated = parentDateAdded.ToString("d"),
            SearchableText = $"{addon.Name}{addon.FolderName}{parentName}",
            ToggleEnabledCommand = toggleEnabledCommand,
            UpdateModSettingsCommand = updateModSettingsCommand
        };
    }

    internal static Guid AddonRowId(Guid parentModId, string folderName)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(parentModId + ":" + folderName.ToLowerInvariant()));
        return new Guid(hash);
    }

    public void TriggerPropertyChanged(string? propertyName) => OnPropertyChanged(propertyName ?? string.Empty);

    public string SearchableText { get; private set; } = string.Empty;

    public required IAsyncRelayCommand ToggleEnabledCommand { get; init; }
    public required IAsyncRelayCommand UpdateModSettingsCommand { get; set; }
}

/// Referencing nested types does not work from xaml
public partial class ModRowVM_ModNotificationVM(ModNotification modNotification) : ObservableObject
{
    public DateTime Time { get; init; } = modNotification.Time;
    public Guid Id { get; init; } = modNotification.Id;
    public Guid ModId { get; init; } = modNotification.ModId;
    public AttentionType AttentionType { get; init; } = modNotification.AttentionType;
}

public sealed class ModGridSortingMethod : SortingMethod<ModRowVM>
{
    public ModGridSortingMethod(Sorter<ModRowVM> sortingMethodType, ModRowVM? firstItem = null,
        ICollection<ModRowVM>? lastItems = null) : base(sortingMethodType, firstItem, lastItems)
    {
    }

    public static readonly ModRowSorter[] AllSorters =
    [
        ModRowSorter.IsEnabledSorter,
        ModRowSorter.DatedAddedSorter,
        ModRowSorter.DisplayNameSorter,
        ModRowSorter.ModFolderSorter,
        ModRowSorter.AuthSorter,
        ModRowSorter.PresetsSorter
    ];
}

public sealed class ModRowSorter : Sorter<ModRowVM>
{
    private ModRowSorter(string sortingMethodType, SortFunc firstSortFunc, AdditionalSortFunc? secondSortFunc = null,
        AdditionalSortFunc? thirdSortFunc = null) : base(sortingMethodType, firstSortFunc, secondSortFunc,
        thirdSortFunc)
    {
    }

    public static readonly string IsEnabledName = nameof(ModRowVM.IsEnabled);

    public static ModRowSorter IsEnabledSorter { get; } = new(IsEnabledName,
        (mod, isDescending) =>
            isDescending
                ? mod.OrderByDescending(x => x.IsEnabled).ThenByDescending(x => x.DateAdded)
                    .ThenBy(x => x.DisplayName)
                : mod.OrderBy(x => x.IsEnabled).ThenByDescending(x => x.DateAdded)
                    .ThenBy(x => x.DisplayName)
    );


    public static readonly string DateAddedName = nameof(ModRowVM.DateAdded);

    public static ModRowSorter DatedAddedSorter { get; } = new(DateAddedName,
        (mod, isDescending) =>
            isDescending
                ? mod.OrderByDescending(x => (x.DateAdded)).ThenBy(x => x.DisplayName)
                : mod.OrderBy(x => (x.DateAdded)).ThenBy(x => x.DisplayName));


    public static readonly string DisplayName = nameof(ModRowVM.DisplayName);

    public static ModRowSorter DisplayNameSorter { get; } = CreateStringSorter(DisplayName, vm => vm.DisplayName);

    public static readonly string ModFolderName = nameof(ModRowVM.FolderName);

    public static ModRowSorter ModFolderSorter { get; } = CreateStringSorter(ModFolderName, vm => vm.FolderName);


    public static readonly string AuthorName = nameof(ModRowVM.Author);

    public static ModRowSorter AuthSorter { get; } = CreateStringSorter(AuthorName, vm => vm.Author);


    public static readonly string PresetsName = nameof(ModRowVM.Presets);

    public static ModRowSorter PresetsSorter { get; } = new(PresetsName,
        (mod, isDescending) =>
            isDescending
                ? mod.OrderByDescending(x => (x.Presets.Length))
                    .ThenByDescending(x => x.DateAdded)
                    .ThenBy(x => x.DisplayName)
                : mod.OrderBy(x => (x.Presets.Length))
                    .ThenByDescending(x => x.DateAdded)
                    .ThenBy(x => x.DisplayName));


    private static ModRowSorter CreateStringSorter(string name, Func<ModRowVM, string> predicate) => new(name,
        (mod, isDescending) =>
            isDescending
                ? mod.OrderByDescending(predicate).ThenByDescending(x => x.DateAdded)
                : mod.OrderBy(predicate).ThenByDescending(x => x.DateAdded));
}