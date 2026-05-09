using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;

namespace yugiho_tools.Application.Services;

/// <summary>
/// Singleton que mantém o mod ativo (selecionado pelo usuário) acessível
/// para qualquer componente que precise saber a URL das imagens, nome do
/// mod, etc., sem precisar passar por parâmetro.
///
/// Persiste o slug do mod ativo em <see cref="Microsoft.Maui.Storage.Preferences"/>
/// para restauração automática entre sessões — selecionar o mod uma vez
/// na tela de Mods basta para todas as outras telas saberem qual usar.
/// </summary>
public class CurrentModContext
{
    public const string PrefKey = "currentMod.slug";

    private Mod? _current;

    public event Action? Changed;

    public Mod? Current => _current;

    public string  ImageUrlTemplate => _current?.ImageUrlTemplate ?? "";
    public string? CurrentSlug      => _current?.Slug;

    public void Set(Mod? mod)
    {
        if (ReferenceEquals(_current, mod)) return;
        _current = mod;
        // Persiste o slug pra restaurar na próxima inicialização. Vazio
        // significa "nenhum mod ativo".
        Microsoft.Maui.Storage.Preferences.Default.Set(PrefKey, mod?.Slug ?? "");
        Changed?.Invoke();
    }

    /// <summary>
    /// Restaura o mod ativo a partir do slug salvo. No-op se nenhum slug
    /// foi persistido ou se o mod não existe mais (ex.: foi removido).
    /// </summary>
    public async Task RestoreFromPreferencesAsync(IModRepository repo)
    {
        var slug = Microsoft.Maui.Storage.Preferences.Default.Get(PrefKey, "");
        if (string.IsNullOrWhiteSpace(slug)) return;
        try
        {
            var mods = await repo.ListAsync();
            var mod = mods.FirstOrDefault(m =>
                string.Equals(m.Slug, slug, StringComparison.OrdinalIgnoreCase));
            if (mod is not null) Set(mod);
        }
        catch
        {
            // I/O falhou — ignora silenciosamente; usuário pode escolher manualmente.
        }
    }
}
