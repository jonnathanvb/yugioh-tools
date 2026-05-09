using System.Text.Json;

namespace yugiho_tools.Application.Services;

/// <summary>
/// i18n minimalista. Carrega <c>{lang}.json</c> em
/// <c>Resources/Raw/i18n/</c> e expõe <see cref="T(string)"/> pra resolver
/// chaves em runtime. Troca de idioma via <see cref="Language"/> dispara
/// <see cref="Changed"/> pros componentes re-renderizarem.
///
/// Quando uma chave não existe no idioma atual, cai no PT (idioma da
/// codebase) e por fim na própria chave — assim a UI nunca quebra mesmo
/// com tradução incompleta.
/// </summary>
public class LocalizationService
{
    public const string LangEn = "en";
    public const string LangPt = "pt";
    public const string LangEs = "es";

    private static readonly string[] Supported = [LangEn, LangPt, LangEs];
    private const string DefaultLang  = LangPt;
    private const string FallbackLang = LangPt;

    private readonly Dictionary<string, Dictionary<string, string>> _bundles = new();
    private string _language = DefaultLang;
    private bool   _loaded;

    public event Action? Changed;

    public string Language
    {
        get => _language;
        set
        {
            var lang = NormalizeLang(value);
            if (lang == _language) return;
            _language = lang;
            Microsoft.Maui.Storage.Preferences.Default.Set(PrefKey, lang);
            Changed?.Invoke();
        }
    }

    public IReadOnlyList<string> SupportedLanguages => Supported;

    public const string PrefKey = "settings.language";

    public LocalizationService()
    {
        var saved = Microsoft.Maui.Storage.Preferences.Default.Get(PrefKey, DefaultLang);
        _language = NormalizeLang(saved);
    }

    /// <summary>
    /// Busca a tradução pra <paramref name="key"/>. Argumentos opcionais
    /// são interpolados como <c>{0}</c>, <c>{1}</c> via <see cref="string.Format(string, object[])"/>.
    /// </summary>
    public string T(string key, params object[] args)
    {
        EnsureLoaded();

        if (_bundles.TryGetValue(_language, out var current)
            && current.TryGetValue(key, out var v))
            return args.Length > 0 ? string.Format(v, args) : v;

        if (_bundles.TryGetValue(FallbackLang, out var fb)
            && fb.TryGetValue(key, out var fv))
            return args.Length > 0 ? string.Format(fv, args) : fv;

        // Última opção: a própria chave aparece — útil pra detectar
        // strings sem tradução durante desenvolvimento.
        return key;
    }

    /// <summary>Idioma "amigável" pra exibir no seletor.</summary>
    public static string DisplayName(string lang) => lang switch
    {
        LangEn => "English",
        LangPt => "Português",
        LangEs => "Español",
        _      => lang,
    };

    private static string NormalizeLang(string lang)
        => Array.IndexOf(Supported, lang) >= 0 ? lang : DefaultLang;

    /// <summary>Carrega TODOS os bundles na primeira chamada (one-shot).
    /// Bundle é JSON simples chave→string; total ≈ alguns KB por idioma.</summary>
    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        foreach (var lang in Supported)
        {
            try
            {
                using var stream = FileSystem.OpenAppPackageFileAsync($"i18n/{lang}.json")
                                             .GetAwaiter().GetResult();
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                        ?? new Dictionary<string, string>();
                _bundles[lang] = dict;
            }
            catch
            {
                _bundles[lang] = new Dictionary<string, string>();
            }
        }
    }
}
