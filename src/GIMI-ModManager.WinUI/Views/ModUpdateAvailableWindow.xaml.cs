// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using GIMI_ModManager.WinUI.ViewModels.ModPageViewModels;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using System.Linq;
using GIMI_ModManager.Core.Contracts.Services;

namespace GIMI_ModManager.WinUI.Views;

public sealed partial class ModUpdateAvailableWindow : WindowEx
{
    public readonly ModUpdateVM ViewModel;
    public readonly IThemeSelectorService ThemeSelectorService = App.GetService<IThemeSelectorService>();

    public bool WebViewIsAvailable = false;


    private CancellationTokenSource? _cts;

    public ModUpdateAvailableWindow(Guid notificationId)
    {
        _cts = new CancellationTokenSource();
        ViewModel = new ModUpdateVM(notificationId, this, _cts.Token);
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ModUpdateVM.IsVariantMode))
            {
                UpdateDetachZone();
                UpdateCloseButton();
            }
        };
        InitializeComponent();


        if (Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = ThemeSelectorService.Theme;
        }

        Closed += (_, _) =>
        {
            if (_cts is null) return;
            var cts = _cts;
            _cts = null;
            cts?.Cancel();
            cts?.Dispose();
        };

        InitWebView();
    }
    private void CloseCancel_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsVariantMode)
            ViewModel.ToggleVariantModeCommand.Execute(null);
        else
            ViewModel.IgnoreAndCloseCommand.Execute(null);
    }

    private void UpdateCloseButton()
    {
        var localizer = App.GetService<ILanguageLocalizer>();
        CloseCancelButton.Content = ViewModel.IsVariantMode
            ? localizer.GetLocalizedStringOrDefault("CancelButton.Text") ?? "Cancel"
            : "Ignore and Close";
    }

    private void InitWebView()
    {
        try
        {
            CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Exception e)
        {
            return;
        }

        WebViewIsAvailable = true;
        ModPageBrowser.Visibility = Visibility.Visible;

        ModPageBrowser.Loading += async (_, _) =>
        {
            await ModPageBrowser.EnsureCoreWebView2Async();
            ModPageBrowser.CoreWebView2.NavigationCompleted += async (_, _) =>
            {
                ModPageLoadingRing.IsActive = false;
                ViewModel.IsOpenDownloadButtonEnabled = true;

                // This automatically opens the author side pane
                // However, it covered the update date and was annoying so I disabled it
                // TODO: Change CSS to make it not cover the update date
                //if (!_isFirstTimeNavigation) return;

                //_isFirstTimeNavigation = false;
                //await Task.Delay(1000);
                //var script = "document.getElementById('HiddenColumnToggleButton').click();";
                //await ModPageBrowser.CoreWebView2.ExecuteScriptAsync(script);
            };
            ModPageBrowser.CoreWebView2.NavigationStarting += (_, _) => ModPageLoadingRing.IsActive = true;
            var theme = ThemeSelectorService.Theme;
            var webTheme = CoreWebView2PreferredColorScheme.Auto;
            switch (theme)
            {
                case ElementTheme.Light:
                    webTheme = CoreWebView2PreferredColorScheme.Light;
                    break;
                case ElementTheme.Dark:
                    webTheme = CoreWebView2PreferredColorScheme.Dark;
                    break;
                case ElementTheme.Default:
                    break;
            }

            ModPageBrowser.CoreWebView2.Profile.PreferredColorScheme = webTheme;
        };
    }

    private async void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        if (!WebViewIsAvailable)
            return;
        await ModPageBrowser.EnsureCoreWebView2Async();

        if (ModPageBrowser.CoreWebView2.IsDefaultDownloadDialogOpen)
            ModPageBrowser.CoreWebView2.CloseDefaultDownloadDialog();
        else
            ModPageBrowser.CoreWebView2.OpenDefaultDownloadDialog();
    }

    private List<ModFileInfoVm> _draggedAddonFiles = [];

    private void FileList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        foreach (var vm in ViewModel.ModFileInfos) vm.IsDropTarget = false;
        _draggedAddonFiles = e.Items.OfType<ModFileInfoVm>().ToList();
        e.Data.SetText("jasm-addon-nest");
        e.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void FileItem_DragOver(object sender, DragEventArgs e)
    {
        foreach (var vm in ViewModel.ModFileInfos) vm.IsDropTarget = false;
        if (sender is FrameworkElement { DataContext: ModFileInfoVm target })
            target.IsDropTarget = true;
        if (ViewModel.IsVariantMode && e.DataView.AvailableFormats.Contains(StandardDataFormats.Text))
            e.AcceptedOperation = DataPackageOperation.Move;
    }

    private void FileItem_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        foreach (var vm in ViewModel.ModFileInfos) vm.IsDropTarget = false;
        if (sender is FrameworkElement { DataContext: ModFileInfoVm target })
        {
            foreach (var dragged in _draggedAddonFiles)
                ViewModel.AttachAsAddon(dragged, target);
        }
        _draggedAddonFiles.Clear();
        UpdateDetachZone();
    }

    private void FileList_Drop(object sender, DragEventArgs e)
    {
        foreach (var vm in ViewModel.ModFileInfos) vm.IsDropTarget = false;
        // Blank-area drop detaches back to top level (item drops mark Handled above).
        foreach (var dragged in _draggedAddonFiles)
            ViewModel.AttachAsAddon(dragged, null);
        _draggedAddonFiles.Clear();
        UpdateDetachZone();
    }

    private void FileItem_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModFileInfoVm target })
            target.IsDropTarget = false;
    }

    private void DetachAddon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModFileInfoVm target })
            target.AddonParent = null;
        UpdateDetachZone();
    }

    private void DetachZone_DragOver(object sender, DragEventArgs e)
    {
        if (ViewModel.IsVariantMode && e.DataView.AvailableFormats.Contains(StandardDataFormats.Text))
            e.AcceptedOperation = DataPackageOperation.Move;
    }

    private void DetachZone_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        foreach (var vm in ViewModel.ModFileInfos) vm.IsDropTarget = false;
        foreach (var dragged in _draggedAddonFiles)
            ViewModel.AttachAsAddon(dragged, null);
        _draggedAddonFiles.Clear();
        UpdateDetachZone();
    }

    private void UpdateDetachZone()
    {
        DetachDropZone.Visibility = ViewModel.IsVariantMode
            && ViewModel.ModFileInfos.Any(x => x.IsAddonChild)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

}