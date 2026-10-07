using GIMI_ModManager.Core.Services.GameBanana.ApiModels;

namespace GIMI_ModManager.Core.Services.GameBanana.Models;

public class ModPageInfo
{
    public ModPageInfo(ApiModProfile apiModProfile)
    {
        ModId = apiModProfile.ModId.ToString();
        ModPageUrl = Uri.TryCreate(apiModProfile.ModPageUrl, UriKind.Absolute, out var modPageUrl) ? modPageUrl : null;
        ModName = apiModProfile.ModName;
        AuthorName = apiModProfile.Author?.AuthorName;
        Description = apiModProfile.Description;
        GameBananaGameId = apiModProfile.Game?.GameId ?? -1;
        GameBananaGameName = apiModProfile.Game?.Name;
        GameBananaCategoryName = apiModProfile.Category?.Name;
        List<Uri> previewImageUrls = [];
        if (apiModProfile.PreviewMedia is not null)
        {
            foreach (var previewMediaImage in apiModProfile.PreviewMedia.Images)
            {
                var imageUrl = Uri.TryCreate(previewMediaImage.BaseUrl + "/" + previewMediaImage.ImageId,
                    UriKind.Absolute, out var uri)
                    ? uri
                    : null;


                if (imageUrl is null ||
                    imageUrl.Scheme != Uri.UriSchemeHttps ||
                    !imageUrl.Host.Equals("images.gamebanana.com", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                previewImageUrls.Add(imageUrl);
            }
        }

        PreviewImages = previewImageUrls.AsReadOnly();

        Files = apiModProfile.Files?.Select(apiModFileInfo => new ModFileInfo(apiModFileInfo, ModId)).ToList() ??
                [];
    }

    public string ModId { get; init; }
    public Uri? ModPageUrl { get; init; }
    public string? ModName { get; init; }
    public string? AuthorName { get; init; }
    public string? Description { get; init; }

    /// <summary>GameBanana's game row id for this submission (e.g. Genshin = 8552). Used to pick the target game.</summary>
    public int GameBananaGameId { get; init; } = -1;

    /// <summary>Game name as reported by GameBanana (e.g. "Genshin Impact").</summary>
    public string? GameBananaGameName { get; init; }

    /// <summary>Category inside that game — for these games the character (e.g. "Klee") or a section ("Others").</summary>
    public string? GameBananaCategoryName { get; init; }

    public IReadOnlyList<Uri> PreviewImages { get; init; }

    public IReadOnlyList<ModFileInfo> Files { get; init; }
}