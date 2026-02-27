using LibGit2Sharp;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GIMI_ModManager.Core.Services;

public interface ICommunityGamesService
{
    Task<bool> TryUpdateCommunityGamesAsync(string repoUrl, string targetDirectory);
    bool VerifyIntegrity(string targetDirectory, IEnumerable<string> activeGames);
}

public class CommunityGamesService : ICommunityGamesService
{
    private readonly ILogger _logger;

    public CommunityGamesService(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<bool> TryUpdateCommunityGamesAsync(string repoUrl, string targetDirectory)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(targetDirectory) || !Directory.EnumerateFileSystemEntries(targetDirectory).Any())
                {
                    _logger.Information("Cloning community games repo to {TargetDirectory}", targetDirectory);
                    Repository.Clone(repoUrl, targetDirectory);
                    return true;
                }

                if (!Repository.IsValid(targetDirectory))
                {
                    _logger.Warning("Target directory {TargetDirectory} is not a valid git repository. Deleting and re-cloning.", targetDirectory);
                    Directory.Delete(targetDirectory, true);
                    Repository.Clone(repoUrl, targetDirectory);
                    return true;
                }

                _logger.Information("Pulling latest changes for community games at {TargetDirectory}", targetDirectory);
                using var repo = new Repository(targetDirectory);
                var signature = new Signature("JASM User", "user@jasm.local", DateTimeOffset.Now);
                Commands.Pull(repo, signature, new PullOptions());

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to update community games from {RepoUrl}", repoUrl);
                return false;
            }
        });
    }

    public bool VerifyIntegrity(string targetDirectory, IEnumerable<string> activeGames)
    {
        if (!Directory.Exists(targetDirectory))
            return false;

        bool hasAtLeastOneGame = false;

        foreach (var game in activeGames)
        {
            // Assuming the repo root has a "Games" folder, or the root *is* the assets folder.
            var gameDir = Path.Combine(targetDirectory, "Games", game);

            // If the repo doesn't put them in a "Games" folder, fallback to root
            if (!Directory.Exists(gameDir))
                gameDir = Path.Combine(targetDirectory, game);

            var gameJsonPath = Path.Combine(gameDir, "game.json");

            if (File.Exists(gameJsonPath))
            {
                hasAtLeastOneGame = true;
            }
        }

        return hasAtLeastOneGame;
    }
}
