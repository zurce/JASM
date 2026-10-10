using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Models.Options;
using Newtonsoft.Json;
using Serilog;

namespace GIMI_ModManager.WinUI.Services.AppManagement;

public class SelectedGameService
{
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILogger _logger;

    private const string _defaultApplicationDataFolder = "ApplicationData";

    private readonly string _jasmAppDataPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JASM");

    private const string ConfigFile = "game.json";
    private readonly string _configPath;

    private const string Genshin = "Genshin";
    private const string Honkai = "Honkai";
    private const string WuWa = "WuWa";
    private const string ZZZ = "ZZZ";
    private const string Endfield = "Endfield";


    public SelectedGameService(ILocalSettingsService localSettingsService, ILogger logger)
    {
        _localSettingsService = localSettingsService;
        _logger = logger;
        Directory.CreateDirectory(_jasmAppDataPath);
        _configPath = Path.Combine(_jasmAppDataPath, ConfigFile);
    }

    private string GetGameSpecificSettingsFolderName(string game)
    {
        return Path.Combine(_defaultApplicationDataFolder + "_" + game);
    }

    public async Task SetSelectedGame(string game)
    {
        if (!IsValidGame(game))
            throw new ArgumentException("Invalid game name.");

        if (await GetSelectedGameAsync() == game)
            return;

        _localSettingsService.SetApplicationDataFolderName(GetGameSpecificSettingsFolderName(game));
        await SaveSelectedGameAsync(game).ConfigureAwait(false);
    }

    public async Task InitializeAsync()
    {
        if (!File.Exists(_configPath))
        {
            CopyOldAppFolder(Genshin);
            await SaveSelectedGameAsync(Genshin);
        }


        var selectedGame = await GetSelectedGameAsync();

        _localSettingsService.SetApplicationDataFolderName(GetGameSpecificSettingsFolderName(selectedGame));
    }

    public async Task<string> GetSelectedGameAsync()
    {
        if (!File.Exists(_configPath))
            return Genshin;

        var selectedGame = JsonConvert.DeserializeObject<SelectedGameModel>(await File.ReadAllTextAsync(_configPath));

        if (selectedGame == null || !IsValidGame(selectedGame.SelectedGame))
            return Genshin;


        return selectedGame.SelectedGame;
    }

    public async Task<SupportedGames[]> GetNotSelectedGameAsync()
    {
        var selectedGame = await GetSelectedGameAsync();

        return selectedGame switch
        {
            Genshin => [SupportedGames.Honkai, SupportedGames.WuWa, SupportedGames.ZZZ, SupportedGames.Endfield],
            Honkai => [SupportedGames.Genshin, SupportedGames.WuWa, SupportedGames.ZZZ, SupportedGames.Endfield],
            WuWa => [SupportedGames.Genshin, SupportedGames.Honkai, SupportedGames.ZZZ, SupportedGames.Endfield],
            ZZZ => [SupportedGames.Genshin, SupportedGames.Honkai, SupportedGames.WuWa, SupportedGames.Endfield],
            Endfield => [SupportedGames.Genshin, SupportedGames.Honkai, SupportedGames.WuWa, SupportedGames.ZZZ],
            _ => throw new ArgumentOutOfRangeException()
        };
    }


    public Task SaveSelectedGameAsync(string game)
    {
        if (!IsValidGame(game))
            throw new ArgumentException("Invalid game name.");


        var selectedGame = new SelectedGameModel
        {
            SelectedGame = game
        };

        return File.WriteAllTextAsync(_configPath, JsonConvert.SerializeObject(selectedGame, Formatting.Indented));
    }

    /// <summary>
    /// Whether JASM+ has a usable configuration for <paramref name="game"/> — i.e. the app starts "ready" for it
    /// (a 3DMigoto root and a mods folder, both still on disk). Reads that game's own settings folder and
    /// restores the caller's, so it is safe to ask about a game that is not the active one.
    /// Used by the startup gate and by the 1-click "switch game?" prompt.
    /// </summary>
    public async Task<bool> IsJasmInitializedForGameAsync(string game)
    {
        if (!IsValidGame(game))
            throw new ArgumentException("Invalid game name.");

        // The settings service is always pointed at the selected game's folder while the app runs
        // (SelectedGameService.InitializeAsync / SetSelectedGame), so that is what has to be restored.
        // NB: GameScopedSettingsLocation is a full path; SetApplicationDataFolderName takes a folder name.
        var activeGame = await GetSelectedGameAsync();
        var swapped = !string.Equals(activeGame, game, StringComparison.OrdinalIgnoreCase);
        if (swapped)
            _localSettingsService.SetApplicationDataFolderName(GetGameSpecificSettingsFolderName(game));

        try
        {
            var modManagerOptions = await _localSettingsService
                .ReadSettingAsync<ModManagerOptions>(ModManagerOptions.Section);

            return Directory.Exists(modManagerOptions?.ModsFolderPath) &&
                   Directory.Exists(modManagerOptions?.GimiRootFolderPath);
        }
        finally
        {
            // Leaving the shared settings service pointed at another game would corrupt every later read.
            if (swapped)
                _localSettingsService.SetApplicationDataFolderName(GetGameSpecificSettingsFolderName(activeGame));
        }
    }

    private bool IsValidGame(string game)
    {
        if (Enum.TryParse<SupportedGames>(game, out _))
            return true;

        return game is Genshin or Honkai or WuWa;
    }


    private void CopyOldAppFolder(string game)
    {
        var oldAppSettingsFolder = new DirectoryInfo(Path.Combine(_jasmAppDataPath, _defaultApplicationDataFolder));

        if (!oldAppSettingsFolder.Exists)
        {
            _logger.Information("Could not find old app folder. Skipping copy.");
            return;
        }

        _logger.Information("Copying old app settings folder to new name.");

        var newAppSettingsFolder =
            new DirectoryInfo(Path.Combine(_jasmAppDataPath, GetGameSpecificSettingsFolderName(game)));
        newAppSettingsFolder.Create();

        if (newAppSettingsFolder.GetFiles().Any())
        {
            _logger.Information("New app settings folder is not empty. Skipping copy.");
            return;
        }

        foreach (var file in oldAppSettingsFolder.GetFiles())
        {
            file.CopyTo(Path.Combine(newAppSettingsFolder.FullName, file.Name));
        }
    }
}

public class SelectedGameModel
{
    public string SelectedGame { get; set; } = "Genshin";
}