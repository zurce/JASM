using System.Diagnostics.CodeAnalysis;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Services.CommandService.Models;
using GIMI_ModManager.WinUI.Services.AppManagement;
using Microsoft.UI.Xaml.Controls;

namespace GIMI_ModManager.WinUI.Views.Settings;

public sealed partial class CreateCommandView : UserControl, IClosableElement
{
    public ViewModels.SettingsViewModels.CreateCommandViewModel ViewModel { get; } =
        App.GetService<ViewModels.SettingsViewModels.CreateCommandViewModel>();

    public event EventHandler? CloseRequested;

    public CreateCommandView(CreateCommandOptions? options = null)
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            var localizer = App.GetService<ILanguageLocalizer>();
            ExecutableFolderSelector.PlaceHolderText = localizer.GetLocalizedStringOrDefault("CreateCommandView_Executable_PlaceHolderText") ?? "Must either be in $PATH or be an absolute path to the executable (Required)";
            WorkingDirectoryFolderSelector.PlaceHolderText = localizer.GetLocalizedStringOrDefault("CreateCommandView_WorkingDirectory_PlaceHolderText") ?? "When manually specifying a path, the folder must exist";
            var ph1 = localizer.GetLocalizedStringOrDefault("CreateCommandView_CommandName_PlaceholderText");
            if (ph1 != null) CommandNameBox.PlaceholderText = ph1;
            var ph2 = localizer.GetLocalizedStringOrDefault("CreateCommandView_Arguments_PlaceholderText");
            if (ph2 != null) ArgumentsBox.PlaceholderText = ph2;
            await ViewModel.Initialize(options).ConfigureAwait(false);
        };
        ViewModel.CloseRequested += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}

public class CreateCommandOptions
{
    [MemberNotNullWhen(true, nameof(CommandDefinition))]
    public bool IsEditingCommand => CommandDefinition is not null;

    public CommandDefinition? CommandDefinition { get; private set; }


    public bool GameStartCommand { get; private set; }

    public bool GameModelImporterCommand { get; private set; }


    private CreateCommandOptions()
    {
    }


    public static CreateCommandOptions EditCommand(CommandDefinition existingCommandDefinition)
    {
        return new CreateCommandOptions()
        {
            CommandDefinition = existingCommandDefinition
        };
    }


    public static CreateCommandOptions CreateGameCommand()
    {
        return new CreateCommandOptions()
        {
            GameStartCommand = true
        };
    }

    public static CreateCommandOptions CreateModelImporterCommand()
    {
        return new CreateCommandOptions()
        {
            GameModelImporterCommand = true
        };
    }
}