namespace DyndDns.TrayApp.Views;

/// <summary>
/// The message boxes of the app: the title, the buttons and the icon of a question or a report are written here
/// once instead of at every call site. The owner is optional, because the tray has no window to centre a box on.
/// </summary>
internal static class Dialogs
{
    /// <summary>Question with «Да / Нет»; <c>true</c> when the user agreed.</summary>
    public static bool Ask(IWin32Window? owner, string text) =>
        Show(owner, text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    /// <summary>
    /// Question with «Да / Нет / Отмена»: <c>true</c> to agree, <c>false</c> to decline and <c>null</c> when the
    /// user chose to cancel the whole action.
    /// </summary>
    public static bool? AskOrCancel(IWin32Window? owner, string text) =>
        Show(owner, text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) switch
        {
            DialogResult.Yes => true,
            DialogResult.No => false,
            _ => null
        };

    /// <summary>Report of something that failed.</summary>
    public static void Warn(IWin32Window? owner, string text) =>
        Show(owner, text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    /// <summary>Report that tells the user what to do next.</summary>
    public static void Tell(IWin32Window? owner, string text) =>
        Show(owner, text, MessageBoxButtons.OK, MessageBoxIcon.Information);

    private static DialogResult Show(IWin32Window? owner, string text, MessageBoxButtons buttons, MessageBoxIcon icon) =>
        owner is null
            ? MessageBox.Show(text, AppInfo.Name, buttons, icon)
            : MessageBox.Show(owner, text, AppInfo.Name, buttons, icon);
}
