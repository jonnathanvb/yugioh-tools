using System.Runtime.Versioning;
using yugiho_tools.Domain.Interfaces;

namespace yugiho_tools.Infrastructure.Shortcuts;

/// <summary>
/// macOS placeholder de <see cref="IGlobalShortcutService"/>. Não dispara
/// hotkeys porque MacCatalyst em modo sandbox não tem acesso a:
///   - Carbon <c>RegisterEventHotKey</c> (precisa de entitlement &amp; permissão de Acessibilidade)
///   - <c>NSEvent.AddGlobalMonitorForEventsMatchingMask</c> (idem)
///   - GameController/IOKit polling de gamepad (precisa de entitlement <c>com.apple.security.device.usb</c>)
///
/// Como o app foi escolhido pra rodar como Mac unsigned/personal-use, manter
/// no-op evita falhas de permissão e mantém o ciclo de UI funcional. Se
/// quiser hotkey no Mac, é necessário:
///   1. Desabilitar App Sandbox em Entitlements.plist
///   2. Adicionar permissão de Acessibilidade manualmente em
///      System Settings → Privacy &amp; Security → Accessibility
///   3. Trocar este stub por uma impl com Carbon RegisterEventHotKey
///      (P/Invoke /System/Library/Frameworks/Carbon.framework/Carbon).
/// </summary>
[SupportedOSPlatform("maccatalyst")]
public sealed class MacGlobalShortcutService : IGlobalShortcutService
{
    public event Action<string>? Triggered;

    public void SetShortcut(string? combo)
    {
        // No-op no macOS. Triggered nunca dispara — UI deve oferecer botão alternativo.
        _ = combo;
        _ = Triggered; // silencia warning de "evento nunca usado".
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
