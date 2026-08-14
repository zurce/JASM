using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Services.GameBanana.ApiModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Serilog;

namespace GIMI_ModManager.WinUI.Views.Controls;

/// <summary>
/// Editable GameBanana search picker: search box + Search button (Enter re-searches), a results
/// list with an update icon on rows that are likely newer versions, and an "always redownload"
/// checkbox. Used both by the single-mod re-link dialog and the batch orphan repair dialog.
/// </summary>
public sealed partial class GameBananaSearchPicker : UserControl
{
    private readonly ILogger _logger = Log.ForContext<GameBananaSearchPicker>();
    private Func<string, CancellationToken, Task<IReadOnlyList<ApiSearchModResult>>>? _searchFunc;
    private int? _gameRowId;
    private string? _localModFolder;

    public GameBananaSearchPicker()
    {
        InitializeComponent();
        SearchButton.Content = GetLocalized("ModPane_SearchButton.Text") ?? "Search";
        AlwaysRedownloadCheckbox.Content = GetLocalized("ModPane_AlwaysRedownload") ?? "Always redownload the mod";
        NoResultsText.Text = GetLocalized("ModPane_SearchNoResultsInline") ?? "No mods found. Adjust the search terms above and search again.";
        SearchBox.PlaceholderText = GetLocalized("ModPane_SearchBoxPlaceholder") ?? "Edit the search terms...";
    }

    public static string? GetLocalized(string key) =>
        App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault(key);

    /// <summary>Whether the "always redownload" checkbox is shown (hidden in batch repair mode).</summary>
    public bool ShowAlwaysRedownloadCheckbox
    {
        get => AlwaysRedownloadCheckbox.Visibility == Visibility.Visible;
        set => AlwaysRedownloadCheckbox.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Configures the picker for a session.</summary>
    public void Initialize(string initialTerms, int? gameRowId, string? localModFolder,
        Func<string, CancellationToken, Task<IReadOnlyList<ApiSearchModResult>>> searchFunc)
    {
        _searchFunc = searchFunc;
        _gameRowId = gameRowId;
        _localModFolder = localModFolder;
        SearchBox.Text = initialTerms;
        ResultsList.ItemsSource = null;
        AlwaysRedownloadCheckbox.IsChecked = false;
        NoResultsText.Visibility = Visibility.Collapsed;
    }

    /// <summary>The currently selected result row (the host reads this on confirm).</summary>
    public ApiSearchModResult? SelectedResult => (ResultsList.SelectedItem as SearchResultRowVM)?.Result;

    /// <summary>Whether the user asked to always redownload.</summary>
    public bool AlwaysRedownload => AlwaysRedownloadCheckbox.IsChecked == true;

    /// <summary>Displays pre-fetched results without re-searching (used for the initial search).</summary>
    public void SetResults(IReadOnlyList<ApiSearchModResult> results) => Populate(results);

    /// <summary>Runs the initial search for the current terms (no-op if terms are blank).</summary>
    public async Task RunInitialSearchAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            await SearchAsync(SearchBox.Text, ct).ConfigureAwait(true);
    }

    public async Task SearchAsync(string terms, CancellationToken ct = default)
    {
        if (_searchFunc is null || string.IsNullOrWhiteSpace(terms))
            return;

        try
        {
            SearchButton.IsEnabled = false;
            var results = await _searchFunc(terms, ct).ConfigureAwait(true);
            Populate(results);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _logger.Error(e, "GameBanana re-search failed");
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private void Populate(IReadOnlyList<ApiSearchModResult> results)
    {
        var rows = results
            .Where(r => r is not null)
            .Select(r => new SearchResultRowVM(r, IsLikelyUpdate(r)))
            .ToArray();
        ResultsList.ItemsSource = rows;
        NoResultsText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (rows.Length > 0)
            ResultsList.SelectedIndex = 0;
    }

    private bool IsLikelyUpdate(ApiSearchModResult result)
    {
        var newestLocal = GetNewestLocalFileTime();
        return newestLocal != default && result.DateModified > newestLocal;
    }

    private DateTime GetNewestLocalFileTime()
    {
        if (_localModFolder is null)
            return default;

        try
        {
            return Directory.EnumerateFiles(_localModFolder, "*", SearchOption.AllDirectories)
                .Select(File.GetLastWriteTime)
                .OrderByDescending(d => d)
                .FirstOrDefault();
        }
        catch (Exception e)
        {
            _logger.Warning(e, "Failed to read local mod file timestamps for search rows");
            return default;
        }
    }

    private void SearchButton_OnClick(object sender, RoutedEventArgs e) =>
        _ = SearchAsync(SearchBox.Text);

    private void SearchBox_OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        // Enter in the search box re-searches; mark handled so the hosting dialog's default button
        // does not also fire (which would confirm/close it).
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            _ = SearchAsync(SearchBox.Text);
        }
    }

    /// <summary>A search result row shown in the picker.</summary>
    private sealed class SearchResultRowVM
    {
        public SearchResultRowVM(ApiSearchModResult result, bool isLikelyUpdate)
        {
            Result = result;
            IsLikelyUpdate = isLikelyUpdate;
        }

        public ApiSearchModResult Result { get; }
        public string Name => Result.Name ?? string.Empty;
        public bool IsLikelyUpdate { get; }

        public Visibility UpdateIconVisibility =>
            IsLikelyUpdate ? Visibility.Visible : Visibility.Collapsed;

        public Microsoft.UI.Xaml.Media.Brush UpdateIconBrush =>
            new Microsoft.UI.Xaml.Media.SolidColorBrush(
                (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]);

        public string UpdateTooltip =>
            GetLocalized("ModPane_LikelyUpdateTooltip") ??
            "This is a newer file version, your mod will be updated if associated";
    }
}
