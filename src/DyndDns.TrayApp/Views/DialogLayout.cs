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
    /// The client size a window needs so its layout is never clipped: never below the size the window was
    /// designed for, larger when a row asks for more room than that. The window turns it into its minimum size
    /// and applies it, because a window can only measure itself.
    /// </summary>
    public static Size ClientSizeNeeded(Control root, Size designed)
    {
        // PreferredSize already covers the margins and the padding of the root, so it is compared as it is.
        var needed = root.PreferredSize;

        return new Size(
            Math.Max(designed.Width, needed.Width),
            Math.Max(designed.Height, needed.Height));
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
