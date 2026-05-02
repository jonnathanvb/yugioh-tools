namespace yugiho_tools.Application.Services;

public class AppSettings
{
    public const string PrefMaxGridCards     = "settings.maxGridCards";
    public const string PrefShortcutKey      = "settings.shortcutKey";
    public const string PrefShortcutClear    = "settings.shortcut.clearDeck";
    public const string PrefShortcutScan     = "settings.shortcut.scanEmulator";
    public const string PrefShortcutCalc     = "settings.shortcut.calculateFusions";

    public const int    DefaultMaxGridCards = 50;
    public const string DefaultShortcutKey  = "F2";

    public event Action? Changed;

    public int MaxGridCards
    {
        get => Microsoft.Maui.Storage.Preferences.Default.Get(PrefMaxGridCards, DefaultMaxGridCards);
        set { Microsoft.Maui.Storage.Preferences.Default.Set(PrefMaxGridCards, value); Changed?.Invoke(); }
    }

    public string ShortcutKey
    {
        get => Microsoft.Maui.Storage.Preferences.Default.Get(PrefShortcutKey, DefaultShortcutKey);
        set { Microsoft.Maui.Storage.Preferences.Default.Set(PrefShortcutKey, value); Changed?.Invoke(); }
    }

    public bool ShortcutClearDeck
    {
        get => Microsoft.Maui.Storage.Preferences.Default.Get(PrefShortcutClear, true);
        set { Microsoft.Maui.Storage.Preferences.Default.Set(PrefShortcutClear, value); Changed?.Invoke(); }
    }

    public bool ShortcutScanEmulator
    {
        get => Microsoft.Maui.Storage.Preferences.Default.Get(PrefShortcutScan, false);
        set { Microsoft.Maui.Storage.Preferences.Default.Set(PrefShortcutScan, value); Changed?.Invoke(); }
    }

    public bool ShortcutCalculateFusions
    {
        get => Microsoft.Maui.Storage.Preferences.Default.Get(PrefShortcutCalc, false);
        set { Microsoft.Maui.Storage.Preferences.Default.Set(PrefShortcutCalc, value); Changed?.Invoke(); }
    }
}
