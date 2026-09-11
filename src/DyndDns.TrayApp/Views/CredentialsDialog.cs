namespace DyndDns.TrayApp.Views;

/// <summary>
/// Collects the router address and administrator credentials. The address stays editable so the
/// user can correct a manually typed value without restarting the wizard.
/// </summary>
internal sealed class CredentialsDialog : SetupDialogBase
{
    private readonly TextBox _address;
    private readonly TextBox _username;
    private readonly TextBox _password;
    private readonly CheckBox _showPassword;

    /// <param name="routerLabel">How the router is written in the lists: «имя (адрес)» or the address alone.</param>
    public CredentialsDialog(string routerLabel, string address, string username, string password)
        : base("Вход в роутер", new Size(430, 280))
    {
        AddContent(CreateLabel($"Роутер: {routerLabel}"));

        _address = new TextBox { Text = address };
        _username = new TextBox { Text = username };
        _password = new TextBox { Text = password, UseSystemPasswordChar = true };

        var fields = CreateFieldGrid();
        AddField(fields, "Адрес роутера:", _address);
        AddField(fields, "Логин:", _username);
        AddField(fields, "Пароль:", _password);

        _showPassword = new CheckBox
        {
            Text = "Показать пароль",
            AutoSize = true
        };
        _showPassword.CheckedChanged += (_, _) => _password.UseSystemPasswordChar = !_showPassword.Checked;

        AddContent(fields);
        AddContent(_showPassword);

        var okButton = CreateButton("Войти (Enter)", 130);
        okButton.DialogResult = DialogResult.OK;

        var cancelButton = CreateButton("Отмена (Esc)", 115);
        cancelButton.DialogResult = DialogResult.Cancel;

        ButtonBar.Controls.AddRange([okButton, cancelButton]);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        FormClosing += OnFormClosing;

        ApplyContentMinimumSize();
    }

    public string Address => _address.Text.Trim();

    public string Username => _username.Text.Trim();

    public string Password => _password.Text;

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK)
            return;

        if (Address.Length == 0)
        {
            e.Cancel = true;
            _address.Focus();
            return;
        }

        if (Username.Length == 0)
        {
            e.Cancel = true;
            _username.Focus();
            return;
        }

        if (Password.Length == 0)
        {
            e.Cancel = true;
            _password.Focus();
        }
    }
}
