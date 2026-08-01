using CommunityToolkit.Mvvm.Input;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.WinUI.ViewModels.SubVms;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GIMI_ModManager.WinUI.Views.Controls;

public sealed partial class FolderSelector : UserControl
{
    /// <summary>
    /// Milliseconds to wait after the user stops typing before the <see cref="PathChangedEvent"/>
    /// is raised (and thus before the path is re-validated). Typing in a folder path currently
    /// re-validates on every keystroke (clearing + repopulating the error InfoBar list), which is
    /// a negligible but avoidable hot path. Debouncing avoids re-validating per keystroke.
    /// </summary>
    private const int ValidationDebounceMilliseconds = 300;

    private readonly DispatcherQueueTimer _validationDebounceTimer;
    private string? _pendingPathValue;

    public FolderSelector()
    {
        // Create the debounce timer BEFORE InitializeComponent() (and thus before any
        // initial Text binding that could raise TextChanged) so SelectedFolderTextBox_TextChanged
        // never touches an uninitialized timer.
        _validationDebounceTimer = DispatcherQueue.CreateTimer();
        _validationDebounceTimer.Interval = TimeSpan.FromMilliseconds(ValidationDebounceMilliseconds);
        _validationDebounceTimer.Tick += ValidationDebounceTimer_OnTick;

        InitializeComponent();
        BrowseCommand = new AsyncRelayCommand(() => Task.CompletedTask);
        Unloaded += FolderSelector_OnUnloaded;
    }

    public event EventHandler<StringEventArgs>? PathChangedEvent;

    private void FolderSelector_OnUnloaded(object sender, RoutedEventArgs e) => CancelPendingValidation();

    /// <summary>
    /// If a validation-debounce is pending, clears it so subsequent keystrokes
    /// replace (rather than stack) the pending path change. Should be called on
    /// navigation away to avoid a stale validation firing.
    /// </summary>
    private void CancelPendingValidation()
    {
        _validationDebounceTimer.Stop();
        _pendingPathValue = null;
    }


    #region ValidationErrorText

    private static readonly DependencyProperty ValidationErrorTextProperty = DependencyProperty.Register(
        nameof(ValidationErrorText), typeof(ICollection<InfoMessage>), typeof(FolderSelector),
        new PropertyMetadata(default(ICollection<InfoMessage>)));

    public ICollection<InfoMessage> ValidationErrorText
    {
        get => (ICollection<InfoMessage>)GetValue(ValidationErrorTextProperty);
        set => SetValue(ValidationErrorTextProperty, value);
    }

    #endregion

    #region Title

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(FolderSelector), new PropertyMetadata("Folder:"));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    #endregion

    #region IsReadOnly

    private static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(FolderSelector),
        new PropertyMetadata(false, OnIsReadOnlyChanged));

    private static readonly DependencyProperty IsNotReadOnlyProperty = DependencyProperty.Register(
        nameof(IsNotReadOnly), typeof(bool), typeof(FolderSelector), new PropertyMetadata(true));

    /// <summary>
    /// When true the folder text box and Browse button are disabled, making the folder
    /// value read-only. Used to lock the mods folder for an XXMI-managed game.
    /// </summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>Inverse of <see cref="IsReadOnly"/> for x:Bind enabled state.</summary>
    public bool IsNotReadOnly
    {
        get => (bool)GetValue(IsNotReadOnlyProperty);
        set => SetValue(IsNotReadOnlyProperty, value);
    }

    private static void OnIsReadOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((FolderSelector)d).IsNotReadOnly = !(bool)e.NewValue;
    }

    #endregion

    #region Footer

    private static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        nameof(Footer), typeof(string), typeof(FolderSelector), new PropertyMetadata(default(string)));

    public string Footer
    {
        get { return (string)GetValue(FooterProperty); }
        set
        {
            FooterWrapper.Visibility = value.IsNullOrEmpty() ? Visibility.Collapsed : Visibility.Visible;
            SetValue(FooterProperty, value);
        }
    }

    #endregion

    #region SelectedFolderValue

    public static readonly DependencyProperty SelectedFolderValueProperty = DependencyProperty.Register(
        nameof(SelectedFolderValue), typeof(string), typeof(FolderSelector), new PropertyMetadata(default(string)));

    public string SelectedFolderValue
    {
        get => (string)GetValue(SelectedFolderValueProperty);
        set => SetValue(SelectedFolderValueProperty, value);
    }

    #endregion

    #region BrowseCommand

    private static readonly DependencyProperty BrowseCommandProperty = DependencyProperty.Register(
        nameof(BrowseCommand), typeof(IAsyncRelayCommand), typeof(FolderSelector),
        new PropertyMetadata(default));

    public IAsyncRelayCommand BrowseCommand
    {
        get => (IAsyncRelayCommand)GetValue(BrowseCommandProperty);
        set => SetValue(BrowseCommandProperty, value);
    }

    #endregion

    #region PlaceHolderText

    private static readonly DependencyProperty PlaceHolderTextProperty = DependencyProperty.Register(
        nameof(PlaceHolderText), typeof(string), typeof(FolderSelector), new PropertyMetadata(default(string)));

    public string PlaceHolderText
    {
        get { return (string)GetValue(PlaceHolderTextProperty); }
        set { SetValue(PlaceHolderTextProperty, value); }
    }

    #endregion


    private void SelectedFolderTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = (sender as TextBox)?.Text;

        // Debounce: store the latest value and restart the timer so PathChangedEvent
        // fires once the user has stopped typing (~debouce ms), instead of on every keystroke.
        _pendingPathValue = text;
        _validationDebounceTimer.Stop();
        _validationDebounceTimer.Start();
    }

    private void ValidationDebounceTimer_OnTick(DispatcherQueueTimer sender, object args)
    {
        _validationDebounceTimer.Stop();
        var value = _pendingPathValue;
        _pendingPathValue = null;
        PathChangedEvent?.Invoke(this, new StringEventArgs(value));
    }

    public class StringEventArgs : EventArgs
    {
        public StringEventArgs(string? value)
        {
            Value = value;
        }

        public string? Value { get; set; }
    }
}