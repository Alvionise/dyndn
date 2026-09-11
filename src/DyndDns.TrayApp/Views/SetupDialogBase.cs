namespace DyndDns.TrayApp.Views;

/// <summary>
/// Shared shell for the app's dialogs: a resizable, top-most window whose content is laid out by docking
/// rather than by coordinates, so resizing never overlaps or clips anything. A dialog fills
/// <see cref="Content"/> row by row and puts its buttons into <see cref="ButtonBar"/>; the window cannot
/// be shrunk below the size it was designed for, and the control that takes the extra space is marked by
/// adding its row with <c>fillHeight</c>.
/// </summary>
internal abstract class SetupDialogBase : Form
{
    /// <summary>Narrowest a field of a two-column form is allowed to become.</summary>
    private const int FieldMinimumWidth = 160;

    private readonly TableLayoutPanel _root;
    private readonly Size _designedClientSize;

    /// <param name="title">What the dialog shows; the app name is prepended to it.</param>
    protected SetupDialogBase(string title, Size clientSize)
    {
        Text = AppInfo.Title(title);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = clientSize;
        _designedClientSize = clientSize;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;

        _root = DialogLayout.Table(1, 2);
        _root.Padding = new Padding(12);
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Content = DialogLayout.Table(1, 0);
        Content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        ButtonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 8, 0, 0)
        };

        _root.Controls.Add(Content, 0, 0);
        _root.Controls.Add(ButtonBar, 0, 1);
        Controls.Add(_root);
    }

    /// <summary>Rows of the dialog; see <see cref="AddContent"/> for how they are added.</summary>
    protected TableLayoutPanel Content { get; }

    /// <summary>
    /// Sizes the dialog to what its layout needs and locks that size as its minimum, so nothing can ever be
    /// clipped. Call it as the last step of a dialog constructor, once all rows are added.
    /// </summary>
    protected void ApplyContentMinimumSize() => DialogLayout.ApplyMinimumSize(this, _root, _designedClientSize);

    /// <summary>Button strip under the content, filled right to left.</summary>
    protected FlowLayoutPanel ButtonBar { get; }

    /// <summary>
    /// Appends a control as the next content row. Controls that look wrong when stretched (labels,
    /// check boxes) keep their natural height; everything else fills its cell, and
    /// <paramref name="fillHeight"/> gives the row all the free space of the dialog.
    /// </summary>
    protected void AddContent(Control control, bool fillHeight = false)
    {
        var row = Content.RowCount;

        Content.RowCount = row + 1;
        Content.RowStyles.Add(fillHeight
            ? new RowStyle(SizeType.Percent, 100F)
            : new RowStyle(SizeType.AutoSize));

        // A row takes the width of the dialog and the height its content needs; only the control that is given the
        // free space of the dialog fills its cell.
        control.Dock = fillHeight ? DockStyle.Fill : DockStyle.Top;

        Content.Controls.Add(control, 0, row);
    }

    /// <summary>Two-column grid for "label above or beside its field" forms; add it as content with <see cref="AddContent"/>.</summary>
    protected static TableLayoutPanel CreateFieldGrid()
    {
        var grid = DialogLayout.Table(2);

        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        return grid;
    }

    /// <summary>Adds one labelled field to a grid created by <see cref="CreateFieldGrid"/>.</summary>
    protected static void AddField(TableLayoutPanel grid, string labelText, Control field)
    {
        var row = grid.RowCount;

        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 10, 0)
        };

        field.Dock = DockStyle.Fill;
        field.Margin = new Padding(0, 4, 0, 4);

        // A field never becomes unusably narrow: its minimum width keeps the dialog from being resized
        // below the point where the text inside could not be read.
        if (field.MinimumSize.Width < FieldMinimumWidth)
            field.MinimumSize = new Size(FieldMinimumWidth, field.MinimumSize.Height);

        grid.Controls.Add(label, 0, row);
        grid.Controls.Add(field, 1, row);
    }

    protected static Label CreateLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 0, 0, 8)
    };

    protected static Button CreateButton(string text, int width = 110) => new()
    {
        Text = text,
        Width = width,
        Height = 28,
        Margin = new Padding(8, 0, 0, 0)
    };
}
