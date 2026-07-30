using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.Services.CommandService;
using GIMI_ModManager.Core.Services.CommandService.Models;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.WinUI.Services;
using GIMI_ModManager.WinUI.Services.AppManagement;
using GIMI_ModManager.WinUI.Validators;
using GIMI_ModManager.WinUI.Views.Settings;
using Microsoft.UI.Xaml.Controls;

namespace GIMI_ModManager.WinUI.ViewModels.SubVms;

public partial class SimpleSelectProcessDialogVM : ObservableObject
{
    private GenshinProcessManager GenshinProcessManager { get; } = App.GetService<GenshinProcessManager>();
    private ThreeDMigtoProcessManager ThreeDMigtoProcessManager { get; } = App.GetService<ThreeDMigtoProcessManager>();

    private readonly IWindowManagerService _windowManagerService = App.GetService<IWindowManagerService>();

    private readonly CommandService _commandService = App.GetService<CommandService>();

    private readonly IGameService _gameService = App.GetService<IGameService>();


    public PathPicker PathPicker { get; }
    public ContentDialog Dialog { get; set; } = null!;
    public StartType Type { get; set; }

    [ObservableProperty] private string _placeHolderText = "";

    public SimpleSelectProcessDialogVM()
    {
        PathPicker = new PathPicker([new IsValidPathFormat(), new FileExists()]);
        PathPicker.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(PathPicker.Path))
                PathPicker.Validate(PathPicker.Path);
        };
    }

    public async Task InternalStart(IProcessManager processManager, StartType type)
    {
        Type = type;
        await processManager.CheckStatus();
        var localizer = App.GetService<ILanguageLocalizer>();
        PlaceHolderText = type switch
        {
            StartType.Game => localizer.GetLocalizedStringOrDefault("CharactersPage_SelectProcessDialog_GamePlaceholderText") ?? "Select the game executable",
            StartType.ModelImporter => localizer.GetLocalizedStringOrDefault("CharactersPage_SelectProcessDialog_ModelImporterPlaceholderText") ?? "Select the model importer executable",
            _ => throw new ArgumentOutOfRangeException()
        };
        Dialog.PrimaryButtonText = localizer.GetLocalizedStringOrDefault("CharactersPage_SelectProcessDialog_PrimaryButtonText") ?? "Save";
        Dialog.SecondaryButtonText = localizer.GetLocalizedStringOrDefault("CharactersPage_SelectProcessDialog_SecondaryButtonText") ?? "Cancel";

        if (processManager.ProcessStatus == ProcessStatus.NotInitialized)
        {
            PathPicker.Path = null;

            var result = await Dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            PathPicker.Validate(PathPicker.Path);
            if (!PathPicker.IsValid)
                return;

            var commandDefinition = type switch
            {
                StartType.Game => CreateGameCommand(),
                StartType.ModelImporter => CreateModelImporterCommand(),
                _ => throw new ArgumentOutOfRangeException()
            };

            await processManager.SetCommandAsync(commandDefinition);
            return;
        }

        await processManager.StartProcess().ConfigureAwait(false);
    }


    [RelayCommand]
    private async Task BrowseSimpleAsync()
    {
        await PathPicker.BrowseFilePathAsync(App.MainWindow, ".exe").ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ShowAdvancedDialogAsync()
    {
        Dialog.Hide();

        var options = Type switch
        {
            StartType.Game => CreateCommandOptions.CreateGameCommand(),
            StartType.ModelImporter => CreateCommandOptions.CreateModelImporterCommand(),
            _ => throw new ArgumentOutOfRangeException()
        };

        var createCommandView = new CreateCommandView(options: options);
        await _windowManagerService
            .ShowFullScreenDialogAsync(createCommandView, App.MainWindow.Content.XamlRoot, App.MainWindow)
            .ConfigureAwait(false);

        switch (Type)
        {
            case StartType.Game:
                await GenshinProcessManager.TryInitialize();
                break;
            case StartType.ModelImporter:
                await ThreeDMigtoProcessManager.TryInitialize();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }


    private CommandDefinition CreateGameCommand()
    {
        var processPath = PathPicker.Path!;
        var gameOptions = _gameService.GameInfo;

        var execOptions = new CommandExecutionOptions()
        {
            UseShellExecute = true,
            RunAsAdmin = true,
            Command = processPath,
            Arguments = null,
            WorkingDirectory = Path.GetDirectoryName(processPath),
            CreateWindow = true
        };

        var localizer = App.GetService<ILanguageLocalizer>();
        var startPrefix = localizer.GetLocalizedStringOrDefault("CharactersPage_StartGamePrefix") ?? "Start";
        var commandDefinition = new CommandDefinition()
        {
            CommandDisplayName = $"{startPrefix} {gameOptions.GameName}",
            KillOnMainAppExit = false,
            ExecutionOptions = execOptions
        };

        return commandDefinition;
    }


    private CommandDefinition CreateModelImporterCommand()
    {
        var processPath = PathPicker.Path!;
        var gameOptions = _gameService.GameInfo;

        var execOptions = new CommandExecutionOptions()
        {
            UseShellExecute = true,
            RunAsAdmin = true,
            Command = processPath,
            Arguments = null,
            WorkingDirectory = Path.GetDirectoryName(processPath),
            CreateWindow = true
        };

        var localizer = App.GetService<ILanguageLocalizer>();
        var startPrefix = localizer.GetLocalizedStringOrDefault("CharactersPage_StartGamePrefix") ?? "Start";
        var commandDefinition = new CommandDefinition()
        {
            CommandDisplayName = $"{startPrefix} {gameOptions.GameModelImporterName}",
            KillOnMainAppExit = false,
            ExecutionOptions = execOptions
        };

        return commandDefinition;
    }

    public enum StartType
    {
        Game,
        ModelImporter
    }
}