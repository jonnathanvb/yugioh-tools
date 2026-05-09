using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace yugiho_tools.Application.Services;

/// <summary>
/// Serviço unificado de tradução. Dispatcha pra Anthropic (Claude API,
/// pago) ou Ollama (modelo local, grátis) com base em
/// <see cref="AppSettings.TranslationProvider"/>. Mantém o nome herdado
/// (<c>AnthropicTranslationService</c>) pra não quebrar referências —
/// o comportamento agora é multiprovider.
///
/// Estratégia comum:
///   • Batch de N descrições por chamada (default 30) — reduz overhead
///     mantendo o prompt curto o bastante pra modelo pequeno.
///   • Saída em JSON estruturado (array de strings) com instrução
///     explícita pra preservar marcadores <c>&lt;_N_&gt;</c> e
///     <c>|N…|</c>.
///   • Termos técnicos do jogo (ATK, DEF, EARTH, FIRE, EFFECT, SUMMON,
///     POW, TEC etc.) são mantidos em inglês via instrução.
/// </summary>
public class AnthropicTranslationService
{
    private const string ApiVersion = "2023-06-01";
    /// <summary>Tamanho do lote pro Claude (rápido, JSON garantido).</summary>
    private const int    BatchSizeClaude = 30;
    /// <summary>Tamanho menor pro Ollama: modelos pequenos rodando em CPU
    /// têm latência alta por batch (30+s). Lotes pequenos dão feedback
    /// mais rápido pro usuário e limitam o tamanho da resposta.</summary>
    private const int    BatchSizeOllama = 10;
    /// <summary>LM Studio costuma rodar modelos parecidos com Ollama,
    /// e o servidor também serializa por padrão — mesmo critério.</summary>
    private const int    BatchSizeLmStudio = 10;
    /// <summary>DeepL aceita até 50 textos por chamada e tem latência
    /// baixa (~200ms). Mantemos perto do limite pra minimizar overhead.</summary>
    private const int    BatchSizeDeepL = 50;

    private readonly AppSettings _cfg;
    private readonly HttpClient  _http;

    public AnthropicTranslationService(AppSettings cfg)
    {
        _cfg  = cfg;
        // Ollama pode levar 30s+ por batch em CPU; 5min é defesa pra
        // não cancelar antes da hora.
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public bool IsConfigured => _cfg.TranslationConfigured;

    /// <summary>
    /// Traduz <paramref name="texts"/> do inglês pro idioma indicado
    /// por <paramref name="targetLang"/> (código ISO: "pt", "es", …).
    /// Mantém ordem 1:1 e tamanho do array; entradas vazias são
    /// preservadas como vazias.
    /// </summary>
    public async Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> texts,
        string targetLang,
        IProgress<(int done, int total)>? progress = null,
        CancellationToken token = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("Translation provider not configured.");
        if (texts.Count == 0) return Array.Empty<string>();

        var result = new string[texts.Count];

        // Preserva entradas vazias sem gastar token.
        var indexed = new List<(int idx, string text)>();
        for (int i = 0; i < texts.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(texts[i])) result[i] = "";
            else indexed.Add((i, texts[i]));
        }

        // Provider único selecionado pelo usuário em /mods.
        var (batchSize, dispatch) = ProviderFor(targetLang);

        // Quebra em batches indexados (mantém posição original pra
        // poder remontar o resultado em ordem). Cada batch vira uma
        // requisição HTTP independente; cada task escreve em índices
        // DISJUNTOS de `result`.
        int done = 0;
        for (int offset = 0; offset < indexed.Count; offset += batchSize)
        {
            token.ThrowIfCancellationRequested();
            var slice = indexed.Skip(offset).Take(batchSize).ToList();
            var textsTwo = slice.Select(s => s.text).ToList();
            var translated = await dispatch(textsTwo, token);
            for (int j = 0; j < slice.Count && j < translated.Count; j++)
                result[slice[j].idx] = translated[j];
            done += slice.Count;
            progress?.Report((done, indexed.Count));
        }

        // Aqui, todos os slots de `result` estão preenchidos. A
        // persistência (write em data.json) acontece no ModExtractor
        // APÓS este método retornar, num único `WriteAsync` — write
        // único, sem corrida entre threads.
        return result;
    }

    /// <summary>Resolve provider escolhido em <see cref="AppSettings.TranslationProvider"/>
    /// pra um par (batchSize, dispatch). Cada provider tem sua latência
    /// e limites — batch é dimensionado de acordo.</summary>
    private (int BatchSize, Func<List<string>, CancellationToken, Task<List<string>>> Dispatch)
        ProviderFor(string targetLang) => _cfg.TranslationProvider switch
    {
        "ollama"   => (BatchSizeOllama,
                       (slice, ct) => TranslateBatchOllamaAsync(slice, targetLang, _cfg.OllamaBaseUrl, ct)),
        "lmstudio" => (BatchSizeLmStudio,
                       (slice, ct) => TranslateBatchLmStudioAsync(slice, targetLang, ct)),
        "deepl"    => (BatchSizeDeepL,
                       (slice, ct) => TranslateBatchDeepLAsync(slice, targetLang, ct)),
        _          => (BatchSizeClaude,
                       (slice, ct) => TranslateBatchClaudeAsync(slice, targetLang, ct)),
    };

    /// <summary>System prompt comum pra ambos providers — instruções de
    /// tradução pra descrição de carta com marcadores preservados.
    /// Inclui few-shot exemplo pra modelos pequenos (llama3.1:8b,
    /// qwen2.5:7b) seguirem o shape do output. Sem o exemplo, modelos
    /// menores tendem a embrulhar em objeto, traduzir os termos
    /// técnicos, ou simplesmente devolver o input.</summary>
    private static string BuildSystemPrompt(string targetLang)
    {
        var lang = LanguageName(targetLang);
        var ex   = ExampleOutput(targetLang);
        return
            $"You translate Yu-Gi-Oh! Forbidden Memories card descriptions from English to {lang}.\n\n" +
            "Rules:\n" +
            "1. Preserve EXACTLY all special markers: `_N_` and `|N text|`. Keep their position and inner content unchanged.\n" +
            "2. Keep these technical game terms in English (do NOT translate them): ATK, DEF, EARTH, FIRE, WATER, WIND, LIGHT, DARK, EFFECT, SUMMON, POW, TEC, BCD, TYPE, LP, HP.\n" +
            $"3. Every other sentence must be translated to {lang}.\n" +
            "4. Output: a JSON array of strings. Same length and order as input. Nothing else — no object wrapper, no commentary, no markdown.\n\n" +
            "Example:\n" +
            "Input: [\"A legendary dragon with terrifying power.\",\"|5<Effect>| Increase ATK of |7EARTH| monsters by |7800|.\"]\n" +
            $"Output: {ex}";
    }

    /// <summary>Exemplo de saída usado no few-shot. Mantém os marcadores
    /// e termos técnicos inalterados; só o texto natural muda.</summary>
    private static string ExampleOutput(string targetLang) => targetLang switch
    {
        "pt" => "[\"Um dragão lendário com poder aterrorizante.\",\"|5<Effect>| Aumenta o ATK de monstros |7EARTH| em |7800|.\"]",
        "es" => "[\"Un dragón legendario con un poder aterrador.\",\"|5<Effect>| Aumenta el ATK de monstruos |7EARTH| en |7800|.\"]",
        _    => "[\"A legendary dragon with terrifying power.\",\"|5<Effect>| Increase ATK of |7EARTH| monsters by |7800|.\"]",
    };

    // ── Claude (Anthropic API) ─────────────────────────────────────────
    private async Task<List<string>> TranslateBatchClaudeAsync(
        List<string> batch, string targetLang, CancellationToken token)
    {
        var system = BuildSystemPrompt(targetLang);
        var userText = "Translate these card descriptions:\n" +
                       JsonSerializer.Serialize(batch, _writeOpts);

        var request = new ClaudeRequest
        {
            Model     = _cfg.ClaudeModel,
            MaxTokens = 4096,
            System    = system,
            Messages  =
            [
                new ClaudeRequest.Message { Role = "user", Content = userText },
            ],
        };

        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"{_cfg.ClaudeBaseUrl.TrimEnd('/')}/v1/messages");
        req.Headers.Add("x-api-key", _cfg.ClaudeApiKey);
        req.Headers.Add("anthropic-version", ApiVersion);
        req.Content = JsonContent.Create(request, options: _writeOpts);

        using var resp = await _http.SendAsync(req, token);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(token);
            throw new HttpRequestException($"Anthropic API {(int)resp.StatusCode}: {errBody}");
        }

        var raw = await resp.Content.ReadAsStringAsync(token);
        var parsed = JsonSerializer.Deserialize<ClaudeResponse>(raw, _readOpts)
                     ?? throw new InvalidDataException("Empty response from Anthropic.");
        var text = parsed.Content?.FirstOrDefault(c => c.Type == "text")?.Text ?? "";
        var result = ParseJsonArray(text);
        ZeroUntranslatedSlots(batch, result, targetLang);
        return result;
    }

    // ── Ollama (servidor local) ────────────────────────────────────────
    private async Task<List<string>> TranslateBatchOllamaAsync(
        List<string> batch, string targetLang, string baseUrl, CancellationToken token)
    {
        var system = BuildSystemPrompt(targetLang);
        var userText = "Translate these card descriptions:\n" +
                       JsonSerializer.Serialize(batch, _writeOpts);

        // /api/chat = endpoint estilo OpenAI no Ollama. format:"json"
        // força output JSON válido (parser interno do Ollama valida).
        // num_predict limita a saída — sem isso modelos pequenos podem
        // entrar em loop de geração e estourar o timeout.
        var request = new OllamaChatRequest
        {
            Model    = _cfg.OllamaModel,
            Stream   = false,
            Format   = "json",
            Options  = new OllamaOptions { NumPredict = 2048, Temperature = 0.2 },
            Messages =
            [
                new OllamaChatRequest.Message { Role = "system", Content = system },
                new OllamaChatRequest.Message { Role = "user",   Content = userText },
            ],
        };

        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/chat");
        req.Content = JsonContent.Create(request, options: _writeOpts);

        using var resp = await _http.SendAsync(req, token);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(token);
            throw new HttpRequestException(
                $"Ollama {baseUrl} {(int)resp.StatusCode}: {errBody}");
        }

        var raw = await resp.Content.ReadAsStringAsync(token);
        var parsed = JsonSerializer.Deserialize<OllamaChatResponse>(raw, _readOpts)
                     ?? throw new InvalidDataException($"Empty response from Ollama {baseUrl}.");
        var text = parsed.Message?.Content ?? "";
        var result = ParseJsonArray(text);
        ZeroUntranslatedSlots(batch, result, targetLang);
        return result;
    }

    // ── LM Studio (OpenAI-compatible API) ──────────────────────────────
    /// <summary>
    /// LM Studio expõe a API no formato OpenAI Chat Completions
    /// (<c>POST /v1/chat/completions</c>). Diferente do Ollama,
    /// <c>response_format: {"type":"json_object"}</c> é o jeito
    /// canônico de forçar JSON; auth é via Bearer token opcional
    /// (só usa se o usuário configurou).
    /// </summary>
    private async Task<List<string>> TranslateBatchLmStudioAsync(
        List<string> batch, string targetLang, CancellationToken token)
    {
        var system = BuildSystemPrompt(targetLang);
        var userText = "Translate these card descriptions:\n" +
                       JsonSerializer.Serialize(batch, _writeOpts);
        var baseUrl = _cfg.LmStudioBaseUrl;

        // ResponseFormat omitido de propósito: LM Studio não aceita
        // o "json_object" da OpenAI moderna (rejeita com "must be
        // 'json_schema' or 'text'") e gerar um schema completo aqui é
        // mais código que ganho real. O system prompt já força array
        // JSON e o ParseJsonArray tolera shapes alternativas. Se algum
        // dia precisar de garantia mais forte, mudar pra json_schema
        // com items:string.
        var request = new OpenAiChatRequest
        {
            Model       = string.IsNullOrWhiteSpace(_cfg.LmStudioModel) ? "auto" : _cfg.LmStudioModel,
            Stream      = false,
            Temperature = 0.2,
            MaxTokens   = 2048,
            Messages =
            [
                new OpenAiChatRequest.Message { Role = "system", Content = system },
                new OpenAiChatRequest.Message { Role = "user",   Content = userText },
            ],
        };

        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/v1/chat/completions");
        if (!string.IsNullOrWhiteSpace(_cfg.LmStudioApiKey))
        {
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _cfg.LmStudioApiKey);
        }
        req.Content = JsonContent.Create(request, options: _writeOpts);

        using var resp = await _http.SendAsync(req, token);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(token);
            throw new HttpRequestException(
                $"LM Studio {baseUrl} {(int)resp.StatusCode}: {errBody}");
        }

        var raw = await resp.Content.ReadAsStringAsync(token);
        var parsed = JsonSerializer.Deserialize<OpenAiChatResponse>(raw, _readOpts)
                     ?? throw new InvalidDataException($"Empty response from LM Studio {baseUrl}.");
        var text = parsed.Choices?.FirstOrDefault()?.Message?.Content ?? "";
        var result = ParseJsonArray(text);
        ZeroUntranslatedSlots(batch, result, targetLang);
        return result;
    }

    // ── DeepL ──────────────────────────────────────────────────────────
    /// <summary>
    /// DeepL é uma tradução de máquina especializada — diferente dos LLMs,
    /// não tem prompt nem JSON envolvido. API:
    /// <c>POST /v2/translate</c> aceita um array <c>text</c> e retorna
    /// um array <c>translations</c> na mesma ordem.
    ///
    /// Contornos pra esse use case:
    /// • <c>tag_handling=xml</c> + <c>ignore_tags=keep</c>: envolve cada
    ///   marcador <c>&lt;_N_&gt;</c> ou <c>|N…|</c> num pseudo-elemento
    ///   <c>&lt;keep&gt;…&lt;/keep&gt;</c> que o DeepL não traduz, e
    ///   desfazemos depois.
    /// • <c>preserve_formatting=1</c>: mantém pontuação/espacos.
    /// </summary>
    private async Task<List<string>> TranslateBatchDeepLAsync(
        List<string> batch, string targetLang, CancellationToken token)
    {
        // Wrap markers in <keep>...</keep> so DeepL passes them through.
        var protectedTexts = batch.Select(WrapMarkers).ToList();

        var url = (string.Equals(_cfg.DeepLPlan, "pro", StringComparison.OrdinalIgnoreCase)
                    ? "https://api.deepl.com"
                    : "https://api-free.deepl.com")
                  + "/v2/translate";

        var form = new List<KeyValuePair<string, string>>
        {
            new("source_lang", "EN"),
            new("target_lang", DeepLLangCode(targetLang)),
            new("tag_handling", "xml"),
            new("ignore_tags", "keep"),
            new("preserve_formatting", "1"),
        };
        foreach (var t in protectedTexts) form.Add(new("text", t));

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("DeepL-Auth-Key", _cfg.DeepLApiKey);
        req.Content = new FormUrlEncodedContent(form);

        using var resp = await _http.SendAsync(req, token);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(token);
            throw new HttpRequestException(
                $"DeepL {(int)resp.StatusCode}: {errBody}");
        }

        var raw = await resp.Content.ReadAsStringAsync(token);
        var parsed = JsonSerializer.Deserialize<DeepLResponse>(raw, _readOpts)
                     ?? throw new InvalidDataException("Empty response from DeepL.");

        var translations = parsed.Translations ?? new();
        var result = translations
            .Select(t => UnwrapMarkers(t.Text ?? ""))
            .ToList();

        // DeepL é tradução de máquina — não devolve o input cru, então
        // ZeroUntranslated não acrescenta.
        return result;
    }

    /// <summary>Mapeia código ISO interno do app pra código do DeepL.
    /// PT precisa ser <c>PT-BR</c> ou <c>PT-PT</c>; usamos BR (Brazil)
    /// porque a UI já trata como pt-BR.</summary>
    private static string DeepLLangCode(string code) => code.ToLowerInvariant() switch
    {
        "pt" => "PT-BR",
        "es" => "ES",
        "en" => "EN-US",
        _    => code.ToUpperInvariant(),
    };

    /// <summary>Envolve cada marcador no texto com <c>&lt;keep&gt;</c>
    /// pra DeepL ignorar (via tag_handling=xml + ignore_tags=keep).</summary>
    private static string WrapMarkers(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        // <_N_> e |N text| (texto interno será traduzido por DeepL)
        // Pra |N…|, envolvemos só o "|N" prefixo e o "|" sufixo, deixando
        // o texto interno acessível à tradução.
        var step1 = System.Text.RegularExpressions.Regex.Replace(
            s, @"<_\d+_>", m => $"<keep>{m.Value}</keep>");
        var step2 = System.Text.RegularExpressions.Regex.Replace(
            step1, @"\|\d", m => $"<keep>{m.Value}</keep>");
        // Fechamento "|" — só o pipe sozinho (não confundir com o de abertura |N).
        // Aqui simples: substitui qualquer | restante.
        var step3 = step2.Replace("|", "<keep>|</keep>");
        return step3;
    }

    /// <summary>Remove os <c>&lt;keep&gt;</c> que envolvemos em
    /// <see cref="WrapMarkers"/>. DeepL preserva o conteúdo interno.</summary>
    private static string UnwrapMarkers(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Replace("<keep>", "").Replace("</keep>", "");
    }

    private sealed class DeepLResponse
    {
        public List<DeepLTranslation>? Translations { get; set; }
    }

    private sealed class DeepLTranslation
    {
        public string? Text { get; set; }
        [JsonPropertyName("detected_source_language")]
        public string? DetectedSourceLanguage { get; set; }
    }

    /// <summary>Tenta interpretar a resposta como array JSON de strings.
    /// Tolera:
    ///   • code-fence (```json…```)
    ///   • objeto envelopando ({ "translations": [...] })
    ///   • objeto com chaves numéricas ({ "0": "...", "1": "..." })
    /// Em caso de falha de parse OU shape impossível, devolve lista
    /// vazia. Caller (ModExtractor) então não escreve nada em
    /// DescriptionsByLanguage[lang], deixando o fallback do
    /// CardDescriptionText cair pra Description original — assim a UI
    /// não fica poluída com texto não-traduzido.</summary>
    private static List<string> ParseJsonArray(string text)
    {
        text = StripCodeFence(text).Trim();
        if (string.IsNullOrEmpty(text)) return new List<string>();
        try
        {
            // Caso 1: array direto.
            if (text.StartsWith("["))
            {
                return JsonSerializer.Deserialize<List<string>>(text, _readOpts)
                       ?? new List<string>();
            }

            // Caso 2: objeto. Procura primeira propriedade que seja
            // array de string — modelos teimam em embrulhar
            // { "translations": [...] } ou { "result": [...] }.
            using var doc = JsonDocument.Parse(text);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array) continue;
                var list = new List<string>();
                foreach (var el in prop.Value.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String)
                        list.Add(el.GetString() ?? "");
                }
                if (list.Count > 0) return list;
            }

            // Caso 3: objeto com chaves numéricas tipo Python dict
            // ({ "0": "...", "1": "..." }) — extrai em ordem.
            var byKey = new SortedDictionary<int, string>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                if (int.TryParse(prop.Name, out var idx))
                    byKey[idx] = prop.Value.GetString() ?? "";
            }
            if (byKey.Count > 0) return byKey.Values.ToList();
        }
        catch { /* fallthrough */ }
        return new List<string>();
    }

    /// <summary>Heurística de "modelo não traduziu": pra cada slot,
    /// se o output bate idêntico ao input, considera fail e zera. Roda
    /// só quando target != "en" — o resto dos slots restantes ficam
    /// como vieram. Evita poluir DescriptionsByLanguage[pt] com texto
    /// em inglês quando o llama3.1 ignora a instrução.</summary>
    private static void ZeroUntranslatedSlots(
        List<string> input, List<string> output, string targetLang)
    {
        if (targetLang == "en") return;
        int n = Math.Min(input.Count, output.Count);
        for (int i = 0; i < n; i++)
        {
            if (string.IsNullOrWhiteSpace(output[i])) continue;
            // Texto curto (<10 chars) ou só marcadores: aceita igualdade
            // sem nuking — pode ser literalmente o mesmo nos dois idiomas.
            if (input[i].Length < 10) continue;
            if (string.Equals(input[i], output[i], StringComparison.Ordinal))
                output[i] = "";
        }
    }

    private static string LanguageName(string code) => code switch
    {
        "pt" => "Brazilian Portuguese",
        "es" => "Spanish",
        "en" => "English",
        _    => code,
    };

    private static string StripCodeFence(string s)
    {
        if (s.StartsWith("```"))
        {
            int firstNl = s.IndexOf('\n');
            if (firstNl > 0) s = s[(firstNl + 1)..];
            int lastFence = s.LastIndexOf("```");
            if (lastFence >= 0) s = s[..lastFence];
        }
        return s;
    }

    private static readonly JsonSerializerOptions _writeOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
    };
    private static readonly JsonSerializerOptions _readOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── Anthropic Messages API DTOs ────────────────────────────────────
    private sealed class ClaudeRequest
    {
        [JsonPropertyName("model")]      public string Model { get; set; } = "";
        [JsonPropertyName("max_tokens")] public int    MaxTokens { get; set; }
        [JsonPropertyName("system")]     public string? System { get; set; }
        [JsonPropertyName("messages")]   public List<Message> Messages { get; set; } = new();

        public sealed class Message
        {
            [JsonPropertyName("role")]    public string Role    { get; set; } = "";
            [JsonPropertyName("content")] public string Content { get; set; } = "";
        }
    }

    private sealed class ClaudeResponse
    {
        public List<ContentBlock>? Content { get; set; }
    }

    private sealed class ContentBlock
    {
        public string? Type { get; set; }
        public string? Text { get; set; }
    }

    // ── Ollama Chat API DTOs ───────────────────────────────────────────
    private sealed class OllamaChatRequest
    {
        [JsonPropertyName("model")]    public string Model    { get; set; } = "";
        [JsonPropertyName("messages")] public List<Message> Messages { get; set; } = new();
        [JsonPropertyName("stream")]   public bool   Stream   { get; set; }
        [JsonPropertyName("format")]   public string? Format  { get; set; }
        [JsonPropertyName("options")]  public OllamaOptions? Options { get; set; }

        public sealed class Message
        {
            [JsonPropertyName("role")]    public string Role    { get; set; } = "";
            [JsonPropertyName("content")] public string Content { get; set; } = "";
        }
    }

    private sealed class OllamaOptions
    {
        [JsonPropertyName("num_predict")] public int    NumPredict  { get; set; }
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
    }

    private sealed class OllamaChatResponse
    {
        public OllamaMessage? Message { get; set; }
    }

    private sealed class OllamaMessage
    {
        public string? Role    { get; set; }
        public string? Content { get; set; }
    }

    // ── OpenAI-compatible (LM Studio) DTOs ─────────────────────────────
    private sealed class OpenAiChatRequest
    {
        [JsonPropertyName("model")]           public string Model { get; set; } = "";
        [JsonPropertyName("messages")]        public List<Message> Messages { get; set; } = new();
        [JsonPropertyName("stream")]          public bool   Stream  { get; set; }
        [JsonPropertyName("temperature")]     public double Temperature { get; set; }
        [JsonPropertyName("max_tokens")]      public int    MaxTokens { get; set; }
        [JsonPropertyName("response_format")] public OpenAiResponseFormat? ResponseFormat { get; set; }

        public sealed class Message
        {
            [JsonPropertyName("role")]    public string Role    { get; set; } = "";
            [JsonPropertyName("content")] public string Content { get; set; } = "";
        }
    }

    private sealed class OpenAiResponseFormat
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "text";
    }

    private sealed class OpenAiChatResponse
    {
        public List<OpenAiChoice>? Choices { get; set; }
    }

    private sealed class OpenAiChoice
    {
        public OpenAiMessage? Message { get; set; }
    }

    private sealed class OpenAiMessage
    {
        public string? Role    { get; set; }
        public string? Content { get; set; }
    }
}
