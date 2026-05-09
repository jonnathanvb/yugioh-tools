namespace yugiho_tools.Application.Services;

public enum ImageSource
{
    /// <summary>Imagens hospedadas no basededatostea (TEAONLINE) — atalho:
    /// "Tea". Cada mod aponta para uma pasta lá. É o comportamento histórico.</summary>
    Tea = 0,

    /// <summary>Arte extraída do próprio ROM (thumbnail 40×32). Independente
    /// de internet e bate exatamente com o que o jogo mostra — porém em
    /// resolução baixa.</summary>
    Mod = 1,
}

public class AppSettings
{
    public const string PrefMaxGridCards     = "settings.maxGridCards";
    public const string PrefShortcutKey      = "settings.shortcutKey";
    public const string PrefShortcutClear    = "settings.shortcut.clearDeck";
    public const string PrefShortcutScan     = "settings.shortcut.scanEmulator";
    public const string PrefShortcutCalc     = "settings.shortcut.calculateFusions";
    public const string PrefImageSource      = "settings.imageSource";
    public const string PrefDescriptionColors = "settings.descriptionColors";
    public const string PrefClaudeApiKey     = "settings.claude.apiKey";
    public const string PrefClaudeModel      = "settings.claude.model";
    public const string PrefClaudeBaseUrl    = "settings.claude.baseUrl";
    public const string PrefAutoTranslate    = "settings.claude.autoTranslate";
    public const string PrefOllamaBaseUrl    = "settings.ollama.baseUrl";
    public const string PrefOllamaModel      = "settings.ollama.model";
    public const string PrefLmStudioBaseUrl  = "settings.lmstudio.baseUrl";
    public const string PrefLmStudioModel    = "settings.lmstudio.model";
    public const string PrefLmStudioApiKey   = "settings.lmstudio.apiKey";
    public const string PrefDeepLApiKey      = "settings.deepl.apiKey";
    public const string PrefDeepLPlan        = "settings.deepl.plan";
    public const string PrefTranslationProvider = "settings.translation.provider";

    public const int          DefaultMaxGridCards = 50;
    public const string       DefaultShortcutKey  = "F2";
    public const ImageSource  DefaultImageSource  = ImageSource.Tea;
    public const string       DefaultClaudeModel  = "claude-haiku-4-5-20251001";
    public const string       DefaultClaudeBaseUrl = "https://api.anthropic.com";
    public const string       DefaultOllamaBaseUrl = "http://localhost:11434";
    public const string       DefaultOllamaModel   = "llama3.1:8b";
    public const string       DefaultLmStudioBaseUrl = "http://localhost:1234";
    public const string       DefaultLmStudioModel   = "";
    public const string       DefaultDeepLPlan       = "free";  // "free" | "pro"
    public const string       DefaultTranslationProvider = "claude";

    /// <summary>
    /// Paleta usada na descrição das cartas 
    /// </summary>
    public static readonly string[] DefaultDescriptionColors =
    [
        "#cfcfe0",  // 0  default body
        "#FBE837",  // 1  amarelo
        "#1C95F2",  // 2  azul
        "#84188B",  // 3  roxo
        "#FFEB3B",  // 4  amarelo claro
        "#9828AD",  // 5  magenta escuro
        "#F44336",  // 6  vermelho
        "#FFEB3B",  // 7  amarelo claro
        "#bdbdbd",  // 8  cinza claro
        "#80cbc4",  // 9  teal
    ];

    public event Action? Changed;

    public AppSettings()
    {
        // Aplica imediatamente no helper estático — assim, ao iniciar o app,
        // o CardImage já reflete a preferência salva sem esperar a primeira
        // tela de Settings ser aberta.
        Helpers.CardImage.UseModImages = ImageSource == ImageSource.Mod;
    }

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

    public ImageSource ImageSource
    {
        get => (ImageSource)Microsoft.Maui.Storage.Preferences.Default
                    .Get(PrefImageSource, (int)DefaultImageSource);
        set
        {
            Microsoft.Maui.Storage.Preferences.Default.Set(PrefImageSource, (int)value);
            // Sincroniza o helper estático: todas as chamadas de
            // CardImage.Url passam a refletir a nova preferência sem precisar
            // restartar o app.
            Helpers.CardImage.UseModImages = value == ImageSource.Mod;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Paleta de cores 0-9 da descrição. Sempre retorna 10 itens; entradas
    /// inválidas (hex malformado, vazio) caem pro default da posição.
    /// </summary>
    public string[] DescriptionColors
    {
        get
        {
            var raw = Microsoft.Maui.Storage.Preferences.Default.Get(PrefDescriptionColors, "");
            var result = (string[])DefaultDescriptionColors.Clone();
            if (string.IsNullOrEmpty(raw)) return result;
            var parts = raw.Split(';');
            for (int i = 0; i < parts.Length && i < result.Length; i++)
            {
                if (IsValidHex(parts[i])) result[i] = parts[i];
            }
            return result;
        }
        set
        {
            // Garante 10 entradas e formato válido antes de salvar; assim
            // a leitura sempre encontra dados consistentes.
            var safe = (string[])DefaultDescriptionColors.Clone();
            if (value is not null)
            {
                for (int i = 0; i < value.Length && i < safe.Length; i++)
                {
                    if (IsValidHex(value[i])) safe[i] = value[i];
                }
            }
            Microsoft.Maui.Storage.Preferences.Default.Set(PrefDescriptionColors, string.Join(";", safe));
            Changed?.Invoke();
        }
    }

    private static bool IsValidHex(string? s) =>
        !string.IsNullOrEmpty(s) && s.Length == 7 && s[0] == '#'
        && s.AsSpan(1).IndexOfAnyExcept(
            "0123456789abcdefABCDEF".AsSpan()) == -1;

    // ── Anthropic / Claude (tradução automática de descrições) ────────────
    /// <summary>API key do Anthropic. Vazio = tradução desabilitada;
    /// quando preenchida, ModExtractor traduz descrições no cadastro/
    /// re-extração do MOD pra os idiomas suportados pelo app.</summary>
    public string ClaudeApiKey
    {
        get => Microsoft.Maui.Storage.Preferences.Default.Get(PrefClaudeApiKey, "");
        set { Microsoft.Maui.Storage.Preferences.Default.Set(PrefClaudeApiKey, value ?? ""); Changed?.Invoke(); }
    }

    /// <summary>Modelo a usar. Default <c>claude-haiku-4-5</c> — barato e
    /// rápido o suficiente pra texto curto (descrição de carta).</summary>
    public string ClaudeModel
    {
        get => Microsoft.Maui.Storage.Preferences.Default.Get(PrefClaudeModel, DefaultClaudeModel);
        set { Microsoft.Maui.Storage.Preferences.Default.Set(PrefClaudeModel, value ?? DefaultClaudeModel); Changed?.Invoke(); }
    }

    /// <summary>Base URL — útil pra apontar pra proxy/Bedrock/Vertex se o
    /// usuário não usar o endpoint público da Anthropic direto.</summary>
    public string ClaudeBaseUrl
    {
        get => Microsoft.Maui.Storage.Preferences.Default.Get(PrefClaudeBaseUrl, DefaultClaudeBaseUrl);
        set { Microsoft.Maui.Storage.Preferences.Default.Set(PrefClaudeBaseUrl, value ?? DefaultClaudeBaseUrl); Changed?.Invoke(); }
    }

    /// <summary>Quando true e <see cref="ClaudeApiKey"/> está preenchida,
    /// ModExtractor traduz descrições automaticamente ao cadastrar o MOD.</summary>
    public bool AutoTranslateOnExtract
    {
        get => Microsoft.Maui.Storage.Preferences.Default.Get(PrefAutoTranslate, false);
        set { Microsoft.Maui.Storage.Preferences.Default.Set(PrefAutoTranslate, value); Changed?.Invoke(); }
    }

    public bool ClaudeConfigured =>
        !string.IsNullOrWhiteSpace(ClaudeApiKey)
        && !string.IsNullOrWhiteSpace(ClaudeModel);

    /// <summary>URL do Ollama. Um único servidor por integração — pra
    /// paralelizar com mais máquinas, configure também LM Studio
    /// (ou Claude). Cada integração configurada vira um worker paralelo
    /// na tradução.</summary>
    public string OllamaBaseUrl
    {
        get => Microsoft.Maui.Storage.Preferences.Default.Get(PrefOllamaBaseUrl, DefaultOllamaBaseUrl);
        set { Microsoft.Maui.Storage.Preferences.Default.Set(PrefOllamaBaseUrl, value ?? DefaultOllamaBaseUrl); Changed?.Invoke(); }
    }

    /// <summary>Tag do modelo a usar (ex.: <c>llama3.1:8b</c>,
    /// <c>qwen2.5:7b</c>). Tem que estar baixado via <c>ollama pull</c>.</summary>
    public string OllamaModel
    {
        get => Microsoft.Maui.Storage.Preferences.Default.Get(PrefOllamaModel, DefaultOllamaModel);
        set { Microsoft.Maui.Storage.Preferences.Default.Set(PrefOllamaModel, value ?? DefaultOllamaModel); Changed?.Invoke(); }
    }

    public bool OllamaConfigured =>
        !string.IsNullOrWhiteSpace(OllamaBaseUrl)
        && !string.IsNullOrWhiteSpace(OllamaModel);

    /// <summary>URL do servidor LM Studio (OpenAI-compatible API,
    /// default <c>http://localhost:1234</c>).</summary>
    public string LmStudioBaseUrl
    {
        get => Microsoft.Maui.Storage.Preferences.Default.Get(PrefLmStudioBaseUrl, DefaultLmStudioBaseUrl);
        set { Microsoft.Maui.Storage.Preferences.Default.Set(PrefLmStudioBaseUrl, value ?? DefaultLmStudioBaseUrl); Changed?.Invoke(); }
    }

    /// <summary>Nome do modelo carregado no LM Studio (string mostrada
    /// na aba Models do LM Studio). Vazio = primeiro modelo disponível
    /// — alguns servidores aceitam "auto" ou ignoram o campo.</summary>
    public string LmStudioModel
    {
        get => Microsoft.Maui.Storage.Preferences.Default.Get(PrefLmStudioModel, DefaultLmStudioModel);
        set { Microsoft.Maui.Storage.Preferences.Default.Set(PrefLmStudioModel, value ?? ""); Changed?.Invoke(); }
    }

    /// <summary>API key opcional pro LM Studio — só se o servidor estiver
    /// configurado pra exigir Bearer auth. Vazio = sem auth.</summary>
    public string LmStudioApiKey
    {
        get => Microsoft.Maui.Storage.Preferences.Default.Get(PrefLmStudioApiKey, "");
        set { Microsoft.Maui.Storage.Preferences.Default.Set(PrefLmStudioApiKey, value ?? ""); Changed?.Invoke(); }
    }

    public bool LmStudioConfigured =>
        !string.IsNullOrWhiteSpace(LmStudioBaseUrl);

    /// <summary>API key do DeepL. Suporte a Free e Pro — o sufixo da
    /// chave (<c>:fx</c>) indica Free pra detectar automaticamente,
    /// mas o usuário também escolhe explícito em <see cref="DeepLPlan"/>.</summary>
    public string DeepLApiKey
    {
        get => Microsoft.Maui.Storage.Preferences.Default.Get(PrefDeepLApiKey, "");
        set { Microsoft.Maui.Storage.Preferences.Default.Set(PrefDeepLApiKey, value ?? ""); Changed?.Invoke(); }
    }

    /// <summary>Plano DeepL: <c>"free"</c> (default, usa
    /// <c>api-free.deepl.com</c>) ou <c>"pro"</c> (<c>api.deepl.com</c>).</summary>
    public string DeepLPlan
    {
        get => Microsoft.Maui.Storage.Preferences.Default.Get(PrefDeepLPlan, DefaultDeepLPlan);
        set { Microsoft.Maui.Storage.Preferences.Default.Set(PrefDeepLPlan, value ?? DefaultDeepLPlan); Changed?.Invoke(); }
    }

    public bool DeepLConfigured => !string.IsNullOrWhiteSpace(DeepLApiKey);

    /// <summary>Provider único selecionado pelo usuário. Valores:
    /// <c>"claude"</c>, <c>"ollama"</c>, <c>"lmstudio"</c>, <c>"deepl"</c>.
    /// Toda vez que rodar tradução, só esse provider é usado.</summary>
    public string TranslationProvider
    {
        get => Microsoft.Maui.Storage.Preferences.Default.Get(PrefTranslationProvider, DefaultTranslationProvider);
        set { Microsoft.Maui.Storage.Preferences.Default.Set(PrefTranslationProvider, value ?? DefaultTranslationProvider); Changed?.Invoke(); }
    }

    /// <summary>True se o provider escolhido tem o necessário pra rodar.</summary>
    public bool TranslationConfigured => TranslationProvider switch
    {
        "claude"   => ClaudeConfigured,
        "ollama"   => OllamaConfigured,
        "lmstudio" => LmStudioConfigured,
        "deepl"    => DeepLConfigured,
        _          => false,
    };

    /// <summary>Idioma da UI. A persistência efetiva fica no
    /// <see cref="LocalizationService"/> (chave própria); aqui só expomos
    /// pra Settings page poder ler/escrever sem injetar dois serviços.</summary>
    public string Language
    {
        get => Microsoft.Maui.Storage.Preferences.Default.Get(LocalizationService.PrefKey, "pt");
        set
        {
            Microsoft.Maui.Storage.Preferences.Default.Set(LocalizationService.PrefKey, value);
            Changed?.Invoke();
        }
    }
}
