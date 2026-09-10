using System.Drawing;
using System.Windows.Forms;

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

    public CredentialsDialog(string address, string username, string password)
        : base("DyndDns — Вход в роутер", new Size(375, 270))
    {
        Controls.Add(CreateLabel("Адрес роутера:", new Point(15, 18)));

        _address = new TextBox
        {
            Location = new Point(15, 40),
            Width = 345,
            Text = address
        };
        Controls.Add(_address);

        Controls.Add(CreateLabel("Логин:", new Point(15, 74)));

        _username = new TextBox
        {
            Location = new Point(15, 96),
            Width = 345,
            Text = username
        };
        Controls.Add(_username);

        Controls.Add(CreateLabel("Пароль:", new Point(15, 130)));

        _password = new TextBox
        {
            Location = new Point(15, 152),
            Width = 345,
            UseSystemPasswordChar = true,
            Text = password
        };
        Controls.Add(_password);

        _showPassword = new CheckBox
        {
            Text = "Показать пароль",
            Location = new Point(15, 182),
            AutoSize = true
        };
        _showPassword.CheckedChanged += (_, _) => _password.UseSystemPasswordChar = !_showPassword.Checked;
        Controls.Add(_showPassword);

        var okButton = CreateButton("Войти", new Point(175, 220), 90);
        okButton.DialogResult = DialogResult.OK;

        var cancelButton = CreateButton("Отмена", new Point(270, 220), 90);
        cancelButton.DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[] { okButton, cancelButton });
        AcceptButton = okButton;
        CancelButton = cancelButton;

        FormClosing += OnFormClosing;
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
