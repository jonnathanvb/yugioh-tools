namespace yugiho_tools.Domain.Interfaces;

/// <summary>
/// Captures keyboard hotkeys (Win32 RegisterHotKey) and gamepad button presses
/// (XInput) globally — fires even when the application is minimized or in
/// background. Combo format examples:
///   "F2", "Ctrl+Shift+F2", "Pad:A", "Pad:Start", "Pad:DPadUp".
/// </summary>
public interface IGlobalShortcutService : IDisposable
{
    event Action<string>? Triggered;

    /// <summary>Set the active combo. Pass null/empty to disable.</summary>
    void SetShortcut(string? combo);
}
