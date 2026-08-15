using GIMI_ModManager.WinUI.Services;
using Newtonsoft.Json;

namespace GIMI_ModManager.WinUI.Models.Settings;

/// <summary>How JASM+ should handle mod configuration persistence when toggling/hot-swapping mods.</summary>
public enum ModPersistenceMode
{
    /// <summary>No persistence; mods lose presets/configs if hot-swapped while the game is running.</summary>
    None,

    /// <summary>Override the mod's own .ini with the saved config (Version B).</summary>
    FileIni,

    /// <summary>Watch &amp; auto-manage d3dx_user.ini (Version A).</summary>
    UserIniWatchdog
}

public class ModPersistenceSettings
{
    [JsonIgnore] public const string Key = "ModPersistenceSettings";
    [JsonIgnore] public const SettingScope Scope = SettingScope.App;

    public ModPersistenceMode Mode { get; set; } = ModPersistenceMode.None;
}