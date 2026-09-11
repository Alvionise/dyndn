namespace DyndDns.TrayApp.Views;

/// <summary>
/// Queues work for the thread of a control and drops it when the control is gone: the handle can disappear
/// between the caller's check and the queued call, and touching a disposed control throws on whichever thread
/// runs it. The windows that collect data on worker threads share this, so the guard lives in one place.
/// </summary>
internal static class ViewDispatch
{
    /// <summary>
    /// Queues work for the thread that owns the windows when the caller runs on another one. The tray has no
    /// control to post to, so it reaches the dispatcher of the application instead.
    /// </summary>
    public static void Post(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    public static void Post(Control control, Action action)
    {
        try
        {
            if (control.IsDisposed || control.Disposing || !control.IsHandleCreated)
                return;

            control.BeginInvoke(new Action(() =>
            {
                if (control.IsDisposed || control.Disposing || !control.IsHandleCreated)
                    return;

                action();
            }));
        }
        catch (ObjectDisposedException)
        {
            // The control was disposed while the action was on its way; nothing is left to update.
        }
        catch (InvalidOperationException)
        {
            // The same race, reported by BeginInvoke as "the handle is not created".
        }
    }
}
