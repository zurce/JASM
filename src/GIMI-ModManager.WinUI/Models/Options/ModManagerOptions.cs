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
}