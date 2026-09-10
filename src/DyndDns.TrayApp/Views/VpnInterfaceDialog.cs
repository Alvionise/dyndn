using System.Drawing;
using System.Windows.Forms;
using DyndDns.TrayApp.Models;

namespace DyndDns.TrayApp.Views;

/// <summary>
/// Shown when the router reports more than one VPN connection, so the user can pick which one the
/// domain traffic should be routed through.
/// </summary>
internal sealed class VpnInterfaceDialog : SetupDialogBase
{
    private readonly ListBox _interfaces;

    public VpnInterfaceDialog(IReadOnlyList<VpnInterfaceInfo> interfaces, string? currentName)
        : base("DyndDns — Выбор VPN", new Size(470, 250))
    {
        Controls.Add(CreateLabel("Найдено несколько VPN-подключений. Выберите нужное:", new Point(15, 15)));

        _interfaces = new ListBox
        {
            Location = new Point(15, 40),
            Size = new Size(440, 140),
            IntegralHeight = false
        };

        foreach (var item in interfaces)
            _interfaces.Items.Add(item);

        SelectCurrent(currentName);

        if (_interfaces.SelectedIndex < 0 && _interfaces.Items.Count > 0)
            _interfaces.SelectedIndex = 0;

        Controls.Add(_interfaces);

        var okButton = CreateButton("Выбрать", new Point(270, 195));
        okButton.DialogResult = DialogResult.OK;

        var cancelButton = CreateButton("Отмена", new Point(365, 195), 90);
        cancelButton.DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[] { okButton, cancelButton });
        AcceptButton = okButton;
        CancelButton = cancelButton;

        FormClosing += OnFormClosing;
    }

    public VpnInterfaceInfo? SelectedInterface { get; private set; }

    private void SelectCurrent(string? currentName)
    {
        if (string.IsNullOrWhiteSpace(currentName))
            return;

        for (var index = 0; index < _interfaces.Items.Count; index++)
        {
            if (_interfaces.Items[index] is VpnInterfaceInfo info &&
                string.Equals(info.Name, currentName, StringComparison.OrdinalIgnoreCase))
            {
                _interfaces.SelectedIndex = index;
                return;
            }
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK)
            return;

        SelectedInterface = _interfaces.SelectedItem as VpnInterfaceInfo;

        if (SelectedInterface is null)
        {
            e.Cancel = true;
            MessageBox.Show(this, "Выберите VPN-подключение из списка.", "DyndDns", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
