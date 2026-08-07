using Newtonsoft.Json;

namespace GIMI_ModManager.WinUI.Models.Options;

public enum GameSource
{
    Release,
    Community
}

public class ModManagerOptions
{
    [JsonIgnore] public const string Section = "ModManagerOptions";

    public GameSource GameSource { get; set; } = GameSource.Release;
    public string CommunityRepoUrl { get; set; } = "https://github.com/zurce/JASM-Community-Resources";

    public string? GimiRootFolderPath { get; set; }
    public string? ModsFolderPath { get; set; }
    public string? UnloadedModsFolderPath { get; set; }
    public bool CharacterSkinsAsCharacters { get; set; }

    /// <summary>
    /// When true, this game's model-importer root is treated as an XXMI-managed
    /// installation: the mods folder is locked to <c>&lt;importer&gt;\Mods</c>, the legacy
    /// Start Game / Start 3DMigoto buttons are suppressed, and an "Open XXMI" button is
    /// shown instead. Per-user / per-environment (machine-specific); false by default.
    /// </summary>
    public bool TreatAsXXMI { get; set; }
}