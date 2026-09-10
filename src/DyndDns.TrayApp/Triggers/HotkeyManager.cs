using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace DyndDns.TrayApp.Triggers;

/// <summary>
/// Registers a system-wide hotkey. Because the app is tray-only, the hotkey is bound to a
/// dedicated, hidden message-only window created here; its message hook receives WM_HOTKEY.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int WS_DISABLED = 0x08000000;
    private const int SW_HIDE = 0;

    private readonly Action _onHotkey;
    private readonly string _key;
    private readonly string _modifiers;
    private readonly HwndSource _messageSource;
    private readonly HwndSourceHook _messageHook;

    private int _hotkeyId;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public HotkeyManager(Action onHotkey, string key, string modifiers)
    {
        _onHotkey = onHotkey;
        _key = key;
        _modifiers = modifiers;
        _messageHook = WndProc;

        var parameters = new HwndSourceParameters("DyndDnsHotkeyWindow")
        {
            Width = 1,
            Height = 1,
            WindowStyle = WS_DISABLED
        };

        _messageSource = new HwndSource(parameters);
        _messageSource.AddHook(_messageHook);
        ShowWindow(_messageSource.Handle, SW_HIDE);
    }

    public bool Register()
    {
        _hotkeyId = ComputeHotkeyId();

        var virtualKey = KeyInterop.VirtualKeyFromKey(ParseKey(_key));
        return RegisterHotKey(_messageSource.Handle, _hotkeyId, ParseModifiers(_modifiers), (uint)virtualKey);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _hotkeyId)
        {
            _onHotkey();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private int ComputeHotkeyId()
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in $"{_modifiers}:{_key}")
                hash = (hash * 31) + c;

            // Keep the id positive and non-zero so 0 can mean "not registered".
            return (hash & 0x7FFF) | 1;
        }
    }

    private static Key ParseKey(string key) => key switch
    {
        "A" => Key.A, "B" => Key.B, "C" => Key.C, "D" => Key.D,
        "E" => Key.E, "F" => Key.F, "G" => Key.G, "H" => Key.H,
        "I" => Key.I, "J" => Key.J, "K" => Key.K, "L" => Key.L,
        "M" => Key.M, "N" => Key.N, "O" => Key.O, "P" => Key.P,
        "Q" => Key.Q, "R" => Key.R, "S" => Key.S, "T" => Key.T,
        "U" => Key.U, "V" => Key.V, "W" => Key.W, "X" => Key.X,
        "Y" => Key.Y, "Z" => Key.Z,
        "0" => Key.D0, "1" => Key.D1, "2" => Key.D2, "3" => Key.D3,
        "4" => Key.D4, "5" => Key.D5, "6" => Key.D6, "7" => Key.D7,
        "8" => Key.D8, "9" => Key.D9,
        _ => Key.V
    };

    private static uint ParseModifiers(string modifiers)
    {
        uint result = 0;

        if (modifiers.Contains("Control")) result |= 0x0002;
        if (modifiers.Contains("Shift")) result |= 0x0004;
        if (modifiers.Contains("Alt")) result |= 0x0001;
        if (modifiers.Contains("Win")) result |= 0x0008;

        return result;
    }

    public void Dispose()
    {
        if (_hotkeyId != 0)
        {
            UnregisterHotKey(_messageSource.Handle, _hotkeyId);
            _hotkeyId = 0;
        }

        _messageSource.RemoveHook(_messageHook);
        _messageSource.Dispose();
    }
}
