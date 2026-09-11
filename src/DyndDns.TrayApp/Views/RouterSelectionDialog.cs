using DyndDns.TrayApp.Models;

namespace DyndDns.TrayApp.Views;

/// <summary>
/// The scan result, always shown when a router is configured: the user either picks a device from the
/// list or types an address manually.
/// </summary>
internal sealed class RouterSelectionDialog : SetupDialogBase
{
    private readonly ListBox _routerList;
    private readonly TextBox _manualAddress;

    public RouterSelectionDialog(IReadOnlyList<DiscoveredRouter> routers)
        : base("Выбор роутера", new Size(480, 340))
    {
        AddContent(CreateLabel(routers.Count > 0
            ? "Найдены роутеры:"
            : "Роутеры не найдены — введите адрес вручную:"));

        _routerList = new ListBox
        {
            IntegralHeight = false,
            DisplayMember = nameof(DiscoveredRouter.DisplayName)
        };

        foreach (var router in routers)
            _routerList.Items.Add(router);

        if (_routerList.Items.Count > 0)
            _routerList.SelectedIndex = 0;

        // The list owns the free space of the dialog.
        AddContent(_routerList, fillHeight: true);

        AddContent(CreateLabel("Или введите адрес вручную:"));

        _manualAddress = new TextBox();
        AddContent(_manualAddress);

        var okButton = CreateButton("Выбрать (Enter)", 130);
        okButton.DialogResult = DialogResult.OK;

        var cancelButton = CreateButton("Отмена (Esc)", 115);
        cancelButton.DialogResult = DialogResult.Cancel;

        ButtonBar.Controls.AddRange([okButton, cancelButton]);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        FormClosing += OnFormClosing;

        ApplyContentMinimumSize();
    }

    /// <summary>Router picked from the scan; <c>null</c> when the address was typed manually.</summary>
    public DiscoveredRouter? SelectedRouter { get; private set; }

    public string Address { get; private set; } = string.Empty;

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK)
            return;

        var manual = _manualAddress.Text.Trim();

        if (manual.Length > 0)
        {
            Address = manual;
            SelectedRouter = null;
            return;
        }

        if (_routerList.SelectedItem is DiscoveredRouter selected)
        {
            Address = selected.Address;
            SelectedRouter = selected;
            return;
        }

        e.Cancel = true;
        Dialogs.Tell(this, "Выберите роутер из списка или введите адрес.");
    }
}
