using Xunit;

namespace DyndDns.TrayApp.Tests;

/// <summary>
/// The checks a dialog's layout has to pass. The dialogs are built from the same shapes, so the check is written
/// once here and used by the tests of each of them.
/// </summary>
internal static class TestLayout
{
    /// <summary>
    /// An auto-sized container measures a row before the width of its column is known, so a control that turns out
    /// wider than the cell it was given hangs over the border of its parent. Walking the tree catches that: the
    /// explanation under the journal limit sat on the bottom border of its group box exactly this way.
    /// </summary>
    public static void AssertNothingEscapesItsParent(Control parent)
    {
        var area = new Rectangle(Point.Empty, parent.ClientSize);

        foreach (Control child in parent.Controls)
        {
            Assert.True(
                area.Contains(child.Bounds),
                $"{child.GetType().Name} \"{child.Text}\" {child.Bounds} does not fit into {parent.GetType().Name} {area}");

            AssertNothingEscapesItsParent(child);
        }
    }
}
