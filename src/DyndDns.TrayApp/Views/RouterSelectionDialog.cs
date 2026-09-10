using System.Drawing;
using System.Windows.Forms;
using DyndDns.TrayApp.Models;

namespace DyndDns.TrayApp.Views;

/// <summary>
/// Shown when more than one Keenetic answers the scan. The user either picks a device from the
/// list or types an address manually.
/// </summary>
internal sealed class RouterSelectionDialog : SetupDialogBase
{
    private readonly ListBox _routerList;
    private readonly TextBox _manualAddress;

    public RouterSelectionDialog(IReadOnlyList<DiscoveredRouter> routers)
        : base("DyndDns — Выбор роутера", new Size(420, 265))
    {
        Controls.Add(CreateLabel("Найдено несколько роутеров. Выберите нужный:", new Point(15, 15)));

        _routerList = new ListBox
        {
            Location = new Point(15, 40),
            Size = new Size(390, 120),
            IntegralHeight = false
        };

        foreach (var router in routers)
            _routerList.Items.Add(router);

        if (_routerList.Items.Count > 0)
            _routerList.SelectedIndex = 0;

        Controls.Add(_routerList);
        Controls.Add(CreateLabel("Или введите адрес вручную:", new Point(15, 168)));

        _manualAddress = new TextBox
        {
            Location = new Point(15, 190),
            Width = 390
        };
        Controls.Add(_manualAddress);

        var okButton = CreateButton("Выбрать", new Point(200, 225));
        okButton.DialogResult = DialogResult.OK;

        var cancelButton = CreateButton("Отмена", new Point(305, 225));
        cancelButton.DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[] { okButton, cancelButton });
        AcceptButton = okButton;
        CancelButton = cancelButton;

        FormClosing += OnFormClosing;
    }

    public string Address { get; private set; } = string.Empty;

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK)
            return;

        var manual = _manualAddress.Text.Trim();
        if (manual.Length > 0)
        {
            Address = manual;
            return;
        }

        if (_routerList.SelectedItem is DiscoveredRouter selected)
        {
            Address = selected.Address;
            return;
        }

        e.Cancel = true;
        MessageBox.Show(this, "Выберите роутер из списка или введите адрес.", "DyndDns", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
