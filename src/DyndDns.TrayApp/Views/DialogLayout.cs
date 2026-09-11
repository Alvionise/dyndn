namespace DyndDns.TrayApp.Views;

/// <summary>
/// The shapes every window of the app is built from. All of them are auto-sized tables that fill the cell they
/// are given — a window is laid out by docking, never by coordinates — so the same properties are written here
/// once instead of in every builder. Rows and columns stay with the caller, which is the part that differs.
/// </summary>
internal static class DialogLayout
{
    /// <summary>Auto-sized table of the given shape that fills its cell; the caller adds its styles next.</summary>
    public static TableLayoutPanel Table(int columns, int rows = 1) => new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = columns,
        RowCount = rows,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink
    };

    /// <summary>
    /// Gives a window the size its layout needs and locks that size as its minimum, so nothing is ever clipped:
    /// the window never shrinks below the size it was designed for and grows when a row asks for more room.
    /// The layout is measured at the designed size and not on the window as it is found: a row that fills the
    /// remaining width asks for its room only once the window is that wide, so a measurement taken on the narrow
    /// window every form starts as would leave that control out of sight. The measurement is taken once, because
    /// such a row reports the width it was given, i.e. it grows together with the window and repeating the
    /// measurement would only make the window wider without ever settling.
    /// </summary>
    public static void ApplyMinimumSize(Form window, Control root, Size designed)
    {
        window.ClientSize = designed;
        window.PerformLayout();

        // PreferredSize already covers the margins and the padding of the root, so it is used as it is.
        var needed = root.PreferredSize;

        var size = new Size(
            Math.Max(designed.Width, needed.Width),
            Math.Max(designed.Height, needed.Height));

        window.ClientSize = size;

        // The frame and the caption of the window are what a client size turns into on the screen, and the window
        // has just been given exactly that size, so its own size is the minimum it may ever have.
        window.MinimumSize = window.Size;
    }

    /// <summary>Group box that sizes itself to its content; the padding is left to the caller's design.</summary>
    public static GroupBox Group(string text, Padding? padding = null)
    {
        var group = new GroupBox
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        if (padding is { } value)
            group.Padding = value;

        return group;
    }

    /// <summary>
    /// Single column that stacks its children from the top, in the order given, with a space between them: the
    /// way a group of checkboxes with their notes is written.
    /// </summary>
    public static TableLayoutPanel Stack(IReadOnlyList<Control> controls)
    {
        var panel = Table(1, controls.Count);
        panel.Padding = new Padding(8, 4, 8, 8);

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        for (var row = 0; row < controls.Count; row++)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var control = controls[row];
            control.Dock = DockStyle.Top;
            control.Margin = new Padding(0, row == 0 ? 0 : 8, 0, 0);

            panel.Controls.Add(control, 0, row);
        }

        return panel;
    }
}
