using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.WinUI.ViewModels.SettingsViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GIMI_ModManager.WinUI.Views.Settings;

public sealed partial class CommandsSettingsPage : Page
{
    public CommandsSettingsViewModel ViewModel { get; } = App.GetService<CommandsSettingsViewModel>();

    public CommandsSettingsPage()
    {
        this.InitializeComponent();
        Loaded += (_, _) =>
        {
            var localizer = App.GetService<ILanguageLocalizer>();
            var d1 = localizer.GetLocalizedStringOrDefault("CommandsSettingsPage_CreateCommand.Description");
            if (d1 != null) CreateCommandCard.Description = d1;
            var d2 = localizer.GetLocalizedStringOrDefault("CommandsSettingsPage_CommandDefinitions.Description");
            if (d2 != null) CommandDefinitionsExpander.Description = d2;
            var d3 = localizer.GetLocalizedStringOrDefault("CommandsSettingsPage_RunningCommands.Description");
            if (d3 != null) RunningCommandsExpander.Description = d3;
        };
    }
}