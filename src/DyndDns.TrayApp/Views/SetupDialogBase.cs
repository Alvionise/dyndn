using System.Drawing;
using System.Windows.Forms;

namespace DyndDns.TrayApp.Views;

/// <summary>
/// Shared shell for the setup dialogs: a fixed, top-most modal window matching the styling
/// already used by the domain prompt in <c>MainViewModel</c>. Top-most matters because the tray
/// app has no owner window that could keep a dialog in front.
/// </summary>
internal abstract class SetupDialogBase : Form
{
    protected SetupDialogBase(string title, Size clientSize)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = clientSize;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        Font = new Font("Segoe UI", 9F);
    }

    protected static Button CreateButton(string text, Point location, int width = 100) => new()
    {
        Text = text,
        Location = location,
        Width = width,
        Height = 28
    };

    protected static Label CreateLabel(string text, Point location) => new()
    {
        Text = text,
        Location = location,
        AutoSize = true
    };
}
