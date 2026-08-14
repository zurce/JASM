using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;

namespace GIMI_ModManager.WinUI.Helpers;

/// <summary>
/// Tracks whether the Alt key is currently held by polling the key state on a UI dispatcher timer
/// (key events are unreliable for Alt — the KeyUp can be missed when focus is in a text box or a
/// modal dialog). Used to switch UI into "advanced" modes (advanced batch configurations, mod URL
/// refetch, etc.).
/// </summary>
public static class AltKeyTracker
{
    private const int PollIntervalMilliseconds = 120;

    private static DispatcherQueueTimer? _timer;
    private static bool _isActive;

    /// <summary>True while the Alt key is held down.</summary>
    public static bool IsActive => _isActive;

    /// <summary>Raised when the Alt held-state changes (on the UI thread).</summary>
    public static event EventHandler? Changed;

    /// <summary>
    /// Starts the polling timer. Must be called on the UI thread (e.g. from App.OnLaunched).
    /// </summary>
    public static void Start()
    {
        if (_timer is not null)
            return;

        var queue = DispatcherQueue.GetForCurrentThread();
        if (queue is null)
            return;

        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(PollIntervalMilliseconds);
        _timer.Tick += (_, _) => Update();
        _timer.Start();
        Update();
    }

    private static void Update()
    {
        bool down;
        try
        {
            var state = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu);
            down = (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        }
        catch
        {
            down = false;
        }

        SetActive(down);
    }

    private static void SetActive(bool active)
    {
        if (_isActive == active)
            return;
        _isActive = active;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
