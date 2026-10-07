using System.Text.Json.Serialization;

namespace GIMI_ModManager.Core.Services.GameBanana.ApiModels;

public class ApiModProfile
{
    [JsonPropertyName("_idRow")] public int ModId { get; init; } = -1;
    [JsonPropertyName("_sName")] public string? ModName { get; init; }

    [JsonPropertyName("_aSubmitter")] public ApiAuthor? Author { get; init; }
    [JsonPropertyName("_aPreviewMedia")] public ApiImagesRoot? PreviewMedia { get; init; }

    [JsonPropertyName("_sProfileUrl")] public string? ModPageUrl { get; init; }

    [JsonPropertyName("_sText")] public string? Description { get; init; }

    /// <summary>The game this submission belongs to. Used by 1-click installs to pick the target game.</summary>
    [JsonPropertyName("_aGame")] public ApiSubmissionGame? Game { get; init; }

    /// <summary>The category inside the game — for these games, the character (e.g. "Klee") or a section ("Others").</summary>
    [JsonPropertyName("_aCategory")] public ApiSubmissionCategory? Category { get; init; }

    [JsonPropertyName("_aFiles")] public ICollection<ApiModFileInfo>? Files { get; init; }
}

/// <summary>Game row of a submission (<c>_aGame</c> in the GameBanana profile payload).</summary>
public class ApiSubmissionGame
{
    [JsonPropertyName("_idRow")] public int GameId { get; init; } = -1;
    [JsonPropertyName("_sName")] public string? Name { get; init; }
}

/// <summary>Category row of a submission (<c>_aCategory</c>) — the character or section inside the game.</summary>
public class ApiSubmissionCategory
{
    [JsonPropertyName("_idRow")] public int CategoryId { get; init; } = -1;
    [JsonPropertyName("_sName")] public string? Name { get; init; }
}

public sealed class ApiAuthor
{
    [JsonPropertyName("_sName")] public string? AuthorName { get; init; }
    [JsonPropertyName("_sAvatarUrl")] public string? AvatarImageUrl { get; init; }
    [JsonPropertyName("_sProfileUrl")] public string? ProfileUrl { get; init; }
}

public sealed class ApiImagesRoot
{
    [JsonPropertyName("_aImages")] public ApiImageUrl[] Images { get; init; } = [];
}

public sealed class ApiImageUrl
{
    [JsonPropertyName("_sFile")] public string? ImageId { get; init; }
    [JsonPropertyName("_sBaseUrl")] public string? BaseUrl { get; init; }
}