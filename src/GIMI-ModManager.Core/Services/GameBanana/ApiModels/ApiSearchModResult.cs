using System.Text.Json.Serialization;

namespace GIMI_ModManager.Core.Services.GameBanana.ApiModels;

/// <summary>
/// A single result from the GameBanana "Util/Search/Results" API. Used to let a user re-link a
/// mod to its GameBanana page when the stored ModUrl was lost.
/// </summary>
public sealed class ApiSearchModResult
{
    [JsonPropertyName("_idRow")] public int ModId { get; init; } = -1;
    [JsonPropertyName("_sModelName")] public string? ModelName { get; init; }
    [JsonPropertyName("_sName")] public string? Name { get; init; }
    [JsonPropertyName("_sProfileUrl")] public string? ProfileUrl { get; init; }
    [JsonPropertyName("_tsDateModified")] public long DateModifiedUnix { get; init; }

    /// <summary>When the mod was last modified on GameBanana (Unix seconds).</summary>
    public DateTime DateModified =>
        DateModifiedUnix > 0 ? DateTimeOffset.FromUnixTimeSeconds(DateModifiedUnix).DateTime : default;
}

/// <summary>Root object of the GameBanana "Util/Search/Results" API response.</summary>
internal sealed class ApiSearchResponse
{
    [JsonPropertyName("_aRecords")] public ICollection<ApiSearchModResult>? Records { get; init; }
}