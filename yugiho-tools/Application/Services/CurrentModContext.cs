using yugiho_tools.Domain.Entities;

namespace yugiho_tools.Application.Services;

/// <summary>
/// Singleton que mantém o mod ativo (selecionado pelo usuário) acessível
/// para qualquer componente que precise saber a URL das imagens, nome do
/// mod, etc., sem precisar passar por parâmetro.
/// </summary>
public class CurrentModContext
{
    private Mod? _current;

    public event Action? Changed;

    public Mod? Current => _current;

    public string ImageUrlTemplate => _current?.ImageUrlTemplate ?? "";

    public void Set(Mod? mod)
    {
        if (ReferenceEquals(_current, mod)) return;
        _current = mod;
        Changed?.Invoke();
    }
}
