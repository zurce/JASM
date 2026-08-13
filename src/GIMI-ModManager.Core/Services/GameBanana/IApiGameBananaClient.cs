using GIMI_ModManager.Core.Services.GameBanana.ApiModels;
using GIMI_ModManager.Core.Services.GameBanana.Models;

namespace GIMI_ModManager.Core.Services.GameBanana;

public interface IApiGameBananaClient
{
    /// <summary>
    /// Checks if the GameBanana API is reachable.
    /// </summary>
    public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the mod profile from the GameBanana API.
    /// </summary>
    /// <param name="modId">The Game banana's mod Id</param>
    /// <param name="cancellationToken"></param>
    /// <returns>ApiModProfile if mod exists or null</returns>
    public Task<ApiModProfile?> GetModProfileAsync(GbModId modId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the mod files info from the GameBanana API.
    /// </summary>
    /// <param name="modId">The Game banana's mod Id</param>
    /// <param name="cancellationToken"></param>
    /// <returns>ApiModFilesInfo if mod exists or null</returns>
    public Task<ApiModFilesInfo?> GetModFilesInfoAsync(GbModId modId, CancellationToken cancellationToken = default);


    /// <summary>
    /// Gets the mod file info from the GameBanana API.
    /// </summary>
    /// <param name="modId">The Game banana's mod Id</param>
    /// <param name="modFileId">The Game banana's mod files Id</param>
    /// <param name="cancellationToken"></param>
    /// <returns>ApiModFileInfo if file exists or null</returns>
    [Obsolete("Use GetModFilesInfoAsync instead")]
    public Task<ApiModFileInfo?> GetModFileInfoAsync(GbModId modId, GbModFileId modFileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the mod file exists on GameBanana.
    /// </summary>
    public Task<bool> ModFileExists(GbModFileId modFileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches GameBanana for mods matching <paramref name="searchString"/>, optionally scoped to
    /// a game row id. Used to let a user re-link a mod to its GameBanana page.
    /// </summary>
    /// <param name="searchString">The search query (e.g. a mod folder name + character).</param>
    /// <param name="gameRowId">Optional GameBanana game id to narrow results (e.g. Genshin = 8552).</param>
    /// <param name="cancellationToken"></param>
    /// <returns>A list of matching mods (name + profile url).</returns>
    public Task<IReadOnlyList<ApiSearchModResult>> SearchModsAsync(string searchString, int? gameRowId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Download mod file from GameBanana.
    /// </summary>
    /// <param name="modFileId">The Game banana's mod files Id</param>
    /// <param name="destinationFile">File  stream to write the contents to</param>
    /// <param name="progress">Reports to as a percentage from 0 to 100</param>
    /// <param name="cancellationToken">Cancels the download but does not delete the destinationFile</param>
    /// <exception cref="InvalidOperationException">When mod is not found</exception>
    /// <exception cref="HttpRequestException"></exception>
    public Task DownloadModAsync(GbModFileId modFileId, FileStream destinationFile, IProgress<int>? progress,
        CancellationToken cancellationToken = default);
}