using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GIMI_ModManager.WinUI.Helpers;

/// <summary>
/// Runs work behind a modal busy dialog: an indeterminate ring plus a line of status text the work keeps updated.
///
/// Used by long, uninterruptible operations that would otherwise leave the UI silent — the orphan-mod batch
/// repair, and downloading/extracting a mod for a GameBanana 1-click install (a heavy mod takes minutes, and
/// without this the app looks frozen).
///
/// The work receives a status setter; call it from the UI thread only (the dialog's text block is UI state).
/// </summary>
public static class BusyDialog
{
    public static async Task<T> RunAsync<T>(string initialMessage,
        Func<Action<string>, CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        var statusText = new TextBlock
        {
            Text = initialMessage,
            FontSize = 16,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.WrapWholeWords,
            MaxWidth = 380
        };

        var dialog = new ContentDialog
        {
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 16,
                MinWidth = 320,
                Children =
                {
                    new ProgressRing { IsActive = true, Width = 48, Height = 48 },
                    statusText
                }
            }
        };

        try
        {
            // Start the work first, then show the (modal) dialog over it. The dialog's show task is
            // observed so a fault during shutdown / dialog teardown does not become an unhandled
            // exception (which would pop the app's error windows).
            var workTask = work(message => statusText.Text = message, ct);
            var showTask = dialog.ShowAsync().AsTask();
            _ = showTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

            return await workTask;
        }
        finally
        {
            try
            {
                dialog.Hide();
            }
            catch
            {
                // ignore if the dialog was not fully opened yet / already closed
            }
        }
    }
}