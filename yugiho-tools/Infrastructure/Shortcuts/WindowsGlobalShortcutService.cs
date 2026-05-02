using System.Runtime.InteropServices;
using yugiho_tools.Domain.Interfaces;

namespace yugiho_tools.Infrastructure.Shortcuts;

/// <summary>
/// Global hotkey listener:
///   - Keyboard combos via Win32 <c>RegisterHotKey</c> on a dedicated thread
///     with its own message loop (works while the app is minimized).
///   - Gamepad buttons via XInput polling on a background timer.
/// </summary>
public class WindowsGlobalShortcutService : IGlobalShortcutService
{
    public event Action<string>? Triggered;

    private readonly object _lock = new();
    private string? _combo;
    private bool    _disposed;

    // ───────── Keyboard (Win32 RegisterHotKey) ─────────
    private const uint MOD_ALT       = 0x0001;
    private const uint MOD_CONTROL   = 0x0002;
    private const uint MOD_SHIFT     = 0x0004;
    private const uint MOD_NOREPEAT  = 0x4000;
    private const uint WM_HOTKEY     = 0x0312;
    private const uint WM_APP        = 0x8000;
    private const uint WM_REGISTER   = WM_APP + 1;
    private const uint WM_UNREGISTER = WM_APP + 2;
    private const uint WM_QUIT       = 0x0012;
    private const int  HOTKEY_ID     = 0xB001;

    private Thread?           _kbThread;
    private uint              _kbThreadId;
    private bool              _kbRegistered;
    private volatile string?  _kbCombo;   // read on the KB thread, written on caller thread

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")]
    private static extern int  GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd; public uint message;
        public IntPtr wParam, lParam;
        public uint time; public int pt_x, pt_y;
    }

    // ───────── Gamepad (XInput) ─────────
    private const uint ERROR_SUCCESS = 0;

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState_14(uint dwUserIndex, out XINPUT_STATE state);
    [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState_13(uint dwUserIndex, out XINPUT_STATE state);
    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState_91(uint dwUserIndex, out XINPUT_STATE state);

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte   bLeftTrigger;
        public byte   bRightTrigger;
        public short  sThumbLX, sThumbLY, sThumbRX, sThumbRY;
    }

    private static readonly (ushort Mask, string Name)[] GamepadButtons =
    {
        (0x0001, "DPadUp"),    (0x0002, "DPadDown"),
        (0x0004, "DPadLeft"),  (0x0008, "DPadRight"),
        (0x0010, "Start"),     (0x0020, "Back"),
        (0x0040, "LStick"),    (0x0080, "RStick"),
        (0x0100, "LB"),        (0x0200, "RB"),
        (0x1000, "A"),         (0x2000, "B"),
        (0x4000, "X"),         (0x8000, "Y"),
    };

    private CancellationTokenSource? _padCts;
    private Task?                    _padTask;
    private volatile string?         _padButton; // read on poll thread, written on caller thread

    private static uint XInputGetState(uint userIndex, out XINPUT_STATE state)
    {
        try { return XInputGetState_14(userIndex, out state); }
        catch { }
        try { return XInputGetState_13(userIndex, out state); }
        catch { }
        try { return XInputGetState_91(userIndex, out state); }
        catch { state = default; return 1167; /* ERROR_DEVICE_NOT_CONNECTED */ }
    }

    // ───────── Public API ─────────
    public void SetShortcut(string? combo)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _combo = combo;

            UnregisterKeyboard();
            StopGamepad();
            _padButton = null;

            if (string.IsNullOrWhiteSpace(combo)) return;

            if (combo.StartsWith("Pad:", StringComparison.OrdinalIgnoreCase))
            {
                _padButton = combo[4..].Trim();
                StartGamepad();
            }
            else
            {
                _kbCombo = combo;
                EnsureKbThread();
                PostThreadMessage(_kbThreadId, WM_REGISTER, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }

    // ───────── Keyboard backend ─────────
    private void EnsureKbThread()
    {
        if (_kbThread is { IsAlive: true }) return;
        var ready = new ManualResetEventSlim(false);
        _kbThread = new Thread(() => KbLoop(ready))
        {
            IsBackground = true,
            Name = "GlobalKbHotkey",
        };
        _kbThread.Start();
        ready.Wait(2000);
    }

    private void KbLoop(ManualResetEventSlim ready)
    {
        _kbThreadId = GetCurrentThreadId();
        ready.Set();

        while (true)
        {
            int rc = GetMessage(out var msg, IntPtr.Zero, 0, 0);
            if (rc <= 0) break;

            if (msg.message == WM_HOTKEY)
            {
                var c = _kbCombo;
                if (!string.IsNullOrEmpty(c))
                    Triggered?.Invoke(c!);
            }
            else if (msg.message == WM_REGISTER)
            {
                if (_kbRegistered) UnregisterHotKey(IntPtr.Zero, HOTKEY_ID);
                _kbRegistered = false;
                if (TryParseKeyCombo(_kbCombo, out var mods, out var vk))
                    _kbRegistered = RegisterHotKey(IntPtr.Zero, HOTKEY_ID, mods | MOD_NOREPEAT, vk);
            }
            else if (msg.message == WM_UNREGISTER)
            {
                if (_kbRegistered) UnregisterHotKey(IntPtr.Zero, HOTKEY_ID);
                _kbRegistered = false;
            }
        }
    }

    private void UnregisterKeyboard()
    {
        _kbCombo = null;
        if (_kbThread is { IsAlive: true } && _kbThreadId != 0)
            PostThreadMessage(_kbThreadId, WM_UNREGISTER, IntPtr.Zero, IntPtr.Zero);
    }

    private static bool TryParseKeyCombo(string? combo, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(combo)) return false;

        foreach (var raw in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL": case "CONTROL":     mods |= MOD_CONTROL; break;
                case "SHIFT":                    mods |= MOD_SHIFT;   break;
                case "ALT":                      mods |= MOD_ALT;     break;
                default:
                    if (TryGetVk(raw, out vk)) { /* set */ }
                    else return false;
                    break;
            }
        }
        return vk != 0;
    }

    private static bool TryGetVk(string token, out uint vk)
    {
        vk = 0;
        var k = token.ToUpperInvariant();

        // Letters
        if (k.Length == 1 && k[0] is >= 'A' and <= 'Z') { vk = (uint)k[0]; return true; }
        // Digits
        if (k.Length == 1 && k[0] is >= '0' and <= '9') { vk = (uint)k[0]; return true; }
        // Function keys F1..F24
        if (k.Length >= 2 && k[0] == 'F' && int.TryParse(k.AsSpan(1), out int n) && n >= 1 && n <= 24)
        {
            vk = (uint)(0x70 + (n - 1));
            return true;
        }

        vk = k switch
        {
            " " or "SPACE" or "SPACEBAR" => 0x20,
            "ENTER" or "RETURN"          => 0x0D,
            "ESC" or "ESCAPE"            => 0x1B,
            "TAB"                        => 0x09,
            "BACKSPACE"                  => 0x08,
            "DELETE" or "DEL"            => 0x2E,
            "INSERT" or "INS"            => 0x2D,
            "HOME"                       => 0x24,
            "END"                        => 0x23,
            "PAGEUP"                     => 0x21,
            "PAGEDOWN"                   => 0x22,
            "ARROWLEFT" or "LEFT"        => 0x25,
            "ARROWUP"   or "UP"          => 0x26,
            "ARROWRIGHT" or "RIGHT"      => 0x27,
            "ARROWDOWN" or "DOWN"        => 0x28,
            _ => 0,
        };
        return vk != 0;
    }

    // ───────── Gamepad backend ─────────
    private void StartGamepad()
    {
        // Caller is responsible for ensuring no previous loop is running
        // and for setting _padButton BEFORE this call.
        _padCts = new CancellationTokenSource();
        var token = _padCts.Token;
        _padTask = Task.Run(() => GamepadLoop(token), token);
    }

    private void StopGamepad()
    {
        try
        {
            _padCts?.Cancel();
            _padTask?.Wait(500);
        }
        catch { }
        _padCts?.Dispose();
        _padCts = null;
        _padTask = null;
        // NOTE: do not clear _padButton here — that's owned by SetShortcut.
    }

    private async Task GamepadLoop(CancellationToken token)
    {
        var prev = new ushort[4];
        var prevTrigL = new bool[4];
        var prevTrigR = new bool[4];

        while (!token.IsCancellationRequested)
        {
            for (uint i = 0; i < 4; i++)
            {
                if (XInputGetState(i, out var state) != ERROR_SUCCESS) continue;

                var cur = state.Gamepad.wButtons;
                var newlyPressed = (ushort)(cur & ~prev[i]);
                prev[i] = cur;

                if (newlyPressed != 0)
                {
                    foreach (var (mask, name) in GamepadButtons)
                    {
                        if ((newlyPressed & mask) != 0)
                            Match(name);
                    }
                }

                bool ltDown = state.Gamepad.bLeftTrigger > 30;
                if (ltDown && !prevTrigL[i]) Match("LT");
                prevTrigL[i] = ltDown;

                bool rtDown = state.Gamepad.bRightTrigger > 30;
                if (rtDown && !prevTrigR[i]) Match("RT");
                prevTrigR[i] = rtDown;
            }

            try { await Task.Delay(16, token); }
            catch (TaskCanceledException) { return; }
        }
    }

    private void Match(string buttonName)
    {
        var target = _padButton;
        if (target == null) return;
        if (string.Equals(buttonName, target, StringComparison.OrdinalIgnoreCase))
            Triggered?.Invoke($"Pad:{buttonName}");
    }

    // ───────── Dispose ─────────
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        StopGamepad();
        if (_kbThread is { IsAlive: true } && _kbThreadId != 0)
        {
            try
            {
                PostThreadMessage(_kbThreadId, WM_UNREGISTER, IntPtr.Zero, IntPtr.Zero);
                PostThreadMessage(_kbThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                _kbThread.Join(500);
            }
            catch { }
        }
        GC.SuppressFinalize(this);
    }
}
