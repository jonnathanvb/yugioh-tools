using yugiho_tools.Application.Helpers;
using yugiho_tools.Application.UseCases;
using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;
using yugiho_tools.Domain.ValueObjects;
using yugiho_tools.Infrastructure.Parsing;
using yugiho_tools.Infrastructure.Storage;

namespace yugiho_tools.Application.Services;

/// <summary>
/// Roda a engenharia reversa do MOD UMA vez (na hora do cadastro ou via
/// botão "Extrair") e salva tudo em <c>MOD/{slug}/data.json</c>. Loads
/// posteriores leem o JSON direto — performance dramática em troca de
/// algumas dezenas de MB em disco.
///
/// Reporta progresso em 4 etapas (cada uma 0-25% do total) para o popup
/// poder mostrar texto descritivo do que está acontecendo.
/// </summary>
public class ModExtractor
{
    private readonly LoadRomDataUseCase            _loadRom;
    private readonly IModRepository                _repo;
    private readonly ExtractedDataRepository       _store;
    private readonly LabJsonImporter               _labImporter;
    private readonly AnthropicTranslationService   _translator;
    private readonly LocalizationService           _localization;
    private readonly AppSettings                   _cfg;

    public ModExtractor(
        LoadRomDataUseCase loadRom,
        IModRepository repo,
        ExtractedDataRepository store,
        LabJsonImporter labImporter,
        AnthropicTranslationService translator,
        LocalizationService localization,
        AppSettings cfg)
    {
        _loadRom      = loadRom;
        _repo         = repo;
        _store        = store;
        _labImporter  = labImporter;
        _translator   = translator;
        _localization = localization;
        _cfg          = cfg;
    }

    public record ExtractionStep(int Percent, string Message);

    /// <summary>
    /// Extrai TODOS os dados do MOD e salva em disco. Idempotente —
    /// rodar de novo simplesmente sobrescreve o JSON.
    /// </summary>
    /// <summary>Fonte de onde a Description de cada carta sai. Definido
    /// pelo usuário no cadastro do MOD — agora via <see cref="FieldSourceMatrix"/>
    /// que permite escolha ROM/JSON por campo individual.</summary>
    public async Task<ExtractedRomData> ExtractAsync(
        Mod mod,
        IProgress<ExtractionStep>? progress = null,
        CancellationToken token = default,
        string? externalJsonPath = null,
        bool hasExtraDuelist = false,
        FieldSourceMatrix? sourceMatrix = null)
    {
        Report(0,  "Lendo arquivos do ROM…");
        var binProgress = new Progress<int>(p =>
        {
            int total = (int)(p * 0.7);
            string msg = p < 25 ? "Lendo arquivos do ROM…"
                       : p < 30 ? "Decodificando cartas e duelistas…"
                       : p < 95 ? "Decodificando thumbnails e frames…"
                                : "Finalizando parse…";
            Report(total, msg);
        });

        // SEMPRE rodamos o parser binário porque ele popula os registries
        // estáticos (ModImageDataUrl em cada Card, CardFrameRegistry) que
        // são a base pras imagens gravadas no disco. Se o usuário forneceu
        // JSON externo, vamos sobrescrever os campos de DADOS depois — mas
        // as IMAGENS continuam vindo do parser binário.
        // Mod com duelista extra tem a tabela de
        // dados deslocada -1 slot e contém 40 entradas em vez de 39 — o
        // perfil padrão é override só nesses dois campos.
        var profile = hasExtraDuelist
            ? RomOffsetProfile.Default with
              {
                  DuelistCount = 40,
                  DuelistData  = RomOffsetProfile.Default.DuelistData
                                 - RomOffsetProfile.Default.DuelistStride,
              }
            : null;

        var rom = await Task.Run(() => _loadRom.ExecuteAsync(
            _repo.GetGameFilePath(mod),
            _repo.GetMrgFilePath(mod),
            loadThumbnails: true,
            profile: profile,
            progress: binProgress), token);

        token.ThrowIfCancellationRequested();
        Report(75, "Convertendo pra JSON…");

        var data = BuildDto(mod, rom);

        // Se o usuário passou um JSON externo, usa
        // os DADOS dele pra:
        //   • Drops dos duelistas (lab decifra as criptografias)
        //   • Metadados que NÃO conseguimos parsear do binário ainda:
        //     IsRitual, Limited, Password, CostStars, Rituals
        //
        // Fusões e equips SEMPRE vêm do parser binário (essas tabelas
        // estão sempre íntegras no MRG/SLUS, sem criptografia, e nosso
        // parser cobre todas as variantes de mod). Sobrescrever com o
        // lab pode introduzir off-by-one e fusões que o jogo de fato
        // não tem.
        if (!string.IsNullOrEmpty(externalJsonPath) && File.Exists(externalJsonPath))
        {
            Report(78, "Importando dados do JSON externo…");
            try
            {
                var imported = await _labImporter.ImportAsync(externalJsonPath);
                var matrix = sourceMatrix ?? FieldSourceMatrix.DefaultAllJson;
                MergeImportedMetadata(data, imported, matrix);

                // Duelistas: vêm do JSON só se o usuário escolheu essa
                // fonte. Caso contrário, mantém os parseados do binário.
                if (matrix.Duelists == FieldSource.Json)
                    data.Duelists = imported.Duelists;
            }
            catch (Exception ex)
            {
                // Antes esse erro era engolido silenciosamente e o usuário
                // via o data.json com descrição binária crua sem entender
                // por quê. Agora propaga — o caller (ModExtractionDialog)
                // mostra a falha em vermelho e o usuário sabe que precisa
                // arrumar o JSON antes de continuar.
                throw new InvalidDataException(
                    $"Falha ao importar JSON externo: {ex.Message}", ex);
            }
        }

        // Tradução automática das descrições (opcional). Só roda se o
        // usuário habilitou e o provider escolhido está configurado
        // (Claude precisa de API key; Ollama precisa só estar acessível).
        // Idiomas alvo = todos os suportados pelo app menos "en".
        if (_cfg.AutoTranslateOnExtract && _translator.IsConfigured)
        {
            await TranslateDescriptionsAsync(data, progress, token);
        }

        token.ThrowIfCancellationRequested();
        Report(80, "Gravando data.json…");

        var folder = _repo.GetModFolderPath(mod);
        await _store.WriteAsync(folder, data);

        token.ThrowIfCancellationRequested();
        Report(85, "Salvando imagens das cartas…");
        await Task.Run(() => WriteCardImages(folder, rom.Cards), token);
        await Task.Run(() => WriteThumbnailGrays(folder, rom.Cards), token);

        token.ThrowIfCancellationRequested();
        Report(92, "Salvando molduras…");
        await Task.Run(() => WriteFrameImages(folder), token);

        token.ThrowIfCancellationRequested();
        Report(96, "Salvando ícones (tipo, atributo, guardião)…");
        // Re-lê o MRG aqui pra extrair sprites — em rotinas separadas
        // do parser pra manter o ModExtractor coeso. Custo: 30 MB de I/O
        // que já está em cache do SO depois do parser.
        var mrgPath = _repo.GetMrgFilePath(mod);
        var slusPath = _repo.GetGameFilePath(mod);
        var mrg = await File.ReadAllBytesAsync(mrgPath, token);
        await Task.Run(() => WriteSpriteImages(folder, mrg, hasExtraDuelist), token);

        token.ThrowIfCancellationRequested();
        Report(99, "Removendo arquivos de origem (já não são necessários)…");
        // Depois de extraído, JSON + extracted/ são suficientes pra rodar
        // o app. Apagamos as cópias do SLUS e MRG pra não duplicar dados.
        // Re-extração pede pro usuário fazer upload de novo dos sources.
        TryDelete(slusPath);
        TryDelete(mrgPath);

        Report(100, "Concluído.");
        return data;

        void Report(int pct, string msg) =>
            progress?.Report(new ExtractionStep(pct, msg));
    }

    /// <summary>
    /// Traduz <see cref="ExtractedCard.Description"/> de cada carta pra
    /// todos os idiomas suportados (exceto "en", que é o original) e
    /// guarda em <see cref="ExtractedCard.DescriptionsByLanguage"/>.
    /// Pula cartas que já têm tradução pro idioma — re-extrações não
    /// pagam o custo de novo.
    /// </summary>
    private async Task TranslateDescriptionsAsync(
        ExtractedRomData data,
        IProgress<ExtractionStep>? progress,
        CancellationToken token)
    {
        var targets = _localization.SupportedLanguages
                          .Where(l => l != "en")
                          .ToList();
        if (targets.Count == 0) return;

        foreach (var lang in targets)
        {
            // Filtra cartas que ainda não têm tradução pra esse lang.
            var pending = data.Cards
                .Where(c => !string.IsNullOrWhiteSpace(c.Description)
                            && !c.DescriptionsByLanguage.ContainsKey(lang))
                .ToList();
            if (pending.Count == 0) continue;

            progress?.Report(new ExtractionStep(78,
                $"Traduzindo descrições ({lang}, {pending.Count} cartas)…"));

            try
            {
                // Progresso por batch — caller vê "Traduzindo pt: 30/722"
                // em vez de ficar 1h olhando "Traduzindo descrições..." sem update.
                var batchProgress = new Progress<(int done, int total)>(p =>
                    progress?.Report(new ExtractionStep(78,
                        $"Traduzindo {lang}: {p.done}/{p.total} cartas")));

                var translated = await _translator.TranslateAsync(
                    pending.Select(c => c.Description).ToList(),
                    lang,
                    progress: batchProgress,
                    token: token);

                for (int i = 0; i < pending.Count && i < translated.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(translated[i]))
                        pending[i].DescriptionsByLanguage[lang] = translated[i];
                }
            }
            catch (Exception ex)
            {
                // Tradução é opcional — se a API falhar (sem internet,
                // limit excedido, key inválida) não interrompe o cadastro.
                progress?.Report(new ExtractionStep(78,
                    $"Tradução {lang} falhou ({ex.Message}). Continuando."));
            }
        }
    }

    /// <summary>
    /// Re-traduz um MOD já extraído. Lê o data.json, completa as
    /// descrições nos idiomas faltantes e re-grava. Útil quando o
    /// usuário só configura o Claude depois de cadastrar.
    /// </summary>
    public async Task<bool> TranslateModAsync(
        Mod mod,
        IProgress<ExtractionStep>? progress = null,
        CancellationToken token = default)
    {
        if (!_translator.IsConfigured) return false;

        var folder = _repo.GetModFolderPath(mod);
        var data = await _store.ReadAsync(folder);
        if (data is null) return false;

        await TranslateDescriptionsAsync(data, progress, token);

        progress?.Report(new ExtractionStep(95, "Gravando data.json…"));
        await _store.WriteAsync(folder, data);
        progress?.Report(new ExtractionStep(100, "Concluído."));
        return true;
    }

    /// <summary>
    /// Aplica metadados do JSON do lab por cima dos dados binários, com
    /// granularidade per-campo via <see cref="FieldSourceMatrix"/>.
    ///
    /// Para cada campo configurado como <see cref="FieldSource.Json"/>,
    /// o valor é copiado do JSON. Para <see cref="FieldSource.Rom"/>, o
    /// valor binário (já presente em <paramref name="binary"/>) é mantido.
    /// Campos que só existem no JSON (Limited/Password/CostStars) ficam
    /// vazios/zero quando o usuário escolhe Rom.
    /// </summary>
    private static void MergeImportedMetadata(
        ExtractedRomData binary,
        ExtractedRomData imported,
        FieldSourceMatrix m)
    {
        var byId = imported.Cards.ToDictionary(c => c.Id);
        foreach (var card in binary.Cards)
        {
            if (!byId.TryGetValue(card.Id, out var src)) continue;

            // Name: ROM (binary) é default histórico. Lab pode trazer prefixo
            // de cor "|N…" — útil só se o usuário quer renderização do lab.
            if (m.Name == FieldSource.Json && !string.IsNullOrWhiteSpace(src.Name))
                card.Name = src.Name;

            // Description: ROM = texto binário plano; JSON = pode trazer
            // marcadores ricos (<_N_>, |N…|).
            if (m.Description == FieldSource.Json && !string.IsNullOrWhiteSpace(src.Description))
            {
                card.Description = src.Description;
                // Sincroniza o slot "en" do map de traduções.
                card.DescriptionsByLanguage["en"] = src.Description;
            }

            // Guardian Stars: ROM tem só os originais (1..10); JSON pode
            // trazer Fortuna/Transpluto/Ceres (11..13) em MODs estendidos.
            if (m.Guardians == FieldSource.Json)
            {
                card.Guardian1 = src.Guardian1;
                card.Guardian2 = src.Guardian2;
            }

            // Equips: cada slot é um cardId 1-based no lab; o parser binário
            // já normaliza pra 0-based. JSON pode ter listas mais limpas
            // em MODs com várias edições.
            if (m.Equips == FieldSource.Json)
                card.Equips = src.Equips;

            // Fusions: idem.
            if (m.Fusions == FieldSource.Json)
                card.Fusions = src.Fusions;

            // Rituals: ROM ParseRituals lê 0xB97400 do MRG; JSON tem as
            // receitas decifradas pelo lab. Em MODs com bug de empacotamento
            // o JSON costuma ser mais confiável.
            if (m.Rituals == FieldSource.Json)
                card.Rituals = src.Rituals;
        
            
            
                // IsRitual/IsFusion: SEMPRE derivados pós-merge a partir das
                // listas finais. Antes, mantínhamos "src.IsRitual" como
                // override, mas isso causava bug: quando ROM gerava
                // Rituals=true (carta tinha receita) e JSON dizia
                // IsRitual=false (sem receita), o flag ficava true por
                // herança. Derivar do tamanho da lista pós-merge mantém
                // consistência com a fonte escolhida pelo usuário.
                card.IsRitual = card.Rituals.Any(f => f.Result == card.Id - 1);
                card.IsFusion = card.Fusions.Any(f => f.Result == card.Id - 1);
          

          

            // Campos exclusivos do JSON (não existem no ROM): se o usuário
            // escolheu Rom, esses ficam com o default vazio/zero do
            // BuildDto (card já veio assim).
            if (m.Limited   == FieldSource.Json) card.Limited   = src.Limited;
            if (m.Password  == FieldSource.Json) card.Password  = src.Password;
            if (m.CostStars == FieldSource.Json) card.CostStars = src.CostStars;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* arquivo travado / permissão; ignora silenciosamente */ }
    }

    /// <summary>Converte o resultado do parser binário em DTO serializável.</summary>
    private static ExtractedRomData BuildDto(Mod mod, LoadedRomData rom)
    {
        var cards = rom.Cards.Select(c => new ExtractedCard
        {
            Id          = c.CardId,
            Name        = c.Name,
            Description = c.Description,
            Atk         = c.Attack,
            Def         = c.Defense,
            Lvl         = c.Level,
            Type        = c.CardType,
            Attribute   = c.Attribute,
            Guardian1   = c.GuardianStar1,
            Guardian2   = c.GuardianStar2,
            Equips       = new List<int>(c.Equips),
            EquipTargets = new List<int>(c.EquipTargets),
            Fusions      = ZipFusions(c),
            // Rituals já vem do parser binário (RomParser.ParseRituals).
            // Limited/Password/CostStars só existem quando o usuário
            // fornece JSON externo — defaults vazios aqui.
            IsRitual    = c.IsRitual,
            IsFusion    = c.IsFusion,
            Limited     = c.Limited,
            Password    = c.Password,
            CostStars   = c.CostStars,
            Rituals     = c.Rituals.Select(r => new ExtractedRitual
                          {
                              Ingredients = new List<int>(r.Ingredients),
                              Result      = r.Result,
                          }).ToList(),
            // Já preenche "en" no map de traduções — assim o
            // CardDescriptionText resolve qualquer idioma pelo mesmo
            // caminho (DescriptionsByLanguage[lang]) sem precisar de
            // fallback especial. Vazio quando Description é vazio.
            DescriptionsByLanguage = string.IsNullOrWhiteSpace(c.Description)
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { ["en"] = c.Description },
        }).ToList();

        var duelists = rom.Duelists.Select(d => new ExtractedDuelist
        {
            Id     = d.Id,
            Name   = d.Name,
            Deck   = (ushort[])d.Deck.Clone(),
            SaPow  = (ushort[])d.SaPow.Clone(),
            BcdPow = (ushort[])d.BcdPow.Clone(),
            SaTec  = (ushort[])d.SaTec.Clone(),
        }).ToList();

        // Diagnóstico: soma do pow do duelista 0 ajuda a identificar
        // se os drops estão limpos (≈2048/4096) ou criptografados pelo
        // criptoDrop do TEA (≈2512-2542 — o mod tá protegido).
        int firstPowSum = 0;
        if (rom.Duelists.Count > 0)
            foreach (var v in rom.Duelists[0].SaPow) firstPowSum += v;

        return new ExtractedRomData
        {
            ModSlug  = mod.Slug,
            Cards    = cards,
            Duelists = duelists,
            Positions = new FramePositions
            {
                ArtX  = CardFrameRegistry.ArtX,  ArtY  = CardFrameRegistry.ArtY,
                NameX = CardFrameRegistry.NameX, NameY = CardFrameRegistry.NameY,
                AtkX  = CardFrameRegistry.AtkX,  AtkY  = CardFrameRegistry.AtkY,
                DefX  = CardFrameRegistry.DefX,  DefY  = CardFrameRegistry.DefY,
                StX   = CardFrameRegistry.StX,   StY   = CardFrameRegistry.StY,
                AttrX = CardFrameRegistry.AttrX, AttrY = CardFrameRegistry.AttrY,
            },
            Diagnostics = new ExtractionDiagnostics
            {
                DuelistOffsetUsed         = RomParser.LastDuelistOffsetUsed,
                DuelistOffsetAutoDetected = RomParser.LastDuelistOffsetWasAutoDetected,
                FirstDuelistPowSum        = firstPowSum,
                DropsAppearScrambled      = firstPowSum >= 2480 * 86 && firstPowSum <= 2550 * 86,
            },
        };
    }

    /// <summary>
    /// Comprime as listas paralelas <c>FusionMaterials</c> + <c>FusionResults</c>
    /// (ambas indexadas por posição) numa única lista de pares — formato
    /// mais limpo pra JSON e pra leitura programática.
    /// </summary>
    private static List<ExtractedFusion> ZipFusions(Card c)
    {
        var n = Math.Min(c.FusionMaterials.Count, c.FusionResults.Count);
        var list = new List<ExtractedFusion>(n);
        for (int i = 0; i < n; i++)
        {
            list.Add(new ExtractedFusion
            {
                Material = c.FusionMaterials[i],
                Result   = c.FusionResults[i],
            });
        }
        return list;
    }

    public const string CardsDir      = "extracted/cards";
    public const string FramesDir     = "extracted/frames";
    public const string TypesDir      = "extracted/types";
    public const string GuardiansDir  = "extracted/guardians";
    public const string AttributesDir = "extracted/attributes";
    public const string DuelistsDir   = "extracted/duelists";
    public const string NamesDir      = "extracted/names";
    public const string StarDir       = "extracted/star";
    /// <summary>Bytes brutos das thumbnails grayscale 40×32 — formato
    /// packed: header de 4 bytes (count uint32 LE) + N × 1280 bytes.
    /// Preserva o que o OpenCV detector precisa pra template-match
    /// depois que o MRG já foi apagado no fim da extração.</summary>
    public const string ThumbnailsFile = "extracted/thumbnails.gray";
    private const int   ThumbnailBytes = 40 * 32;

    // ── Offsets dos sprite sheets (descoberto via lab.js bundle) ───────
    // Erro original: confundi as conversões dec→hex. Os valores decimais
    // do lab.js (11862080, 16079232 etc) foram traduzidos com aritmética
    // errada e os bodies saíam deslocados — sprites totalmente garbled.
    // Estes valores agora batem byte-a-byte com as constantes do lab.
    private const int TypesGuardiansBody    = 0xB50040;  // = 11_862_080
    private const int TypesGuardiansClut    = 0xB60200;  // = 11_928_064; +32*m por sprite
    private const int GuardiansSharedClutOff = 768;       // 0xB60200 + 768
    private const int AttributesBody        = 0xF02800;  // = 15_738_880
    private const int AttributesClut        = 0xF08600;  // = 15_762_944; +32*o por atributo
    // Sheet pra types/guardians/attributes/star é 256×128 (não 128×512!).
    // Erro original: confundi a ordem dos args do q1 do lab — interpretei
    // como (width=128, height=256) mas é (height=128, width=256).
    private const int SpriteSheetWidth      = 256;
    private const int SpriteSize            = 16;
    private const int TypesCount            = 24;
    private const int GuardiansCount        = 13;
    private const int AttributesCount       = 9;

    /// <summary>
    /// Grava a arte 102×96 de cada carta como PNG em
    /// <c>extracted/cards/{cardId}.png</c>. Os bytes são extraídos do
    /// data URL gerado pelo parser — só decodifica o base64 e escreve.
    /// </summary>
    private static void WriteCardImages(string modFolder, IReadOnlyList<Card> cards)
    {
        var dir = Path.Combine(modFolder, CardsDir);
        Directory.CreateDirectory(dir);
        // Apaga BMPs antigos pra evitar lixo se o número de cartas mudou.
        foreach (var f in Directory.EnumerateFiles(dir, "*.png")) File.Delete(f);

        foreach (var card in cards)
        {
            if (string.IsNullOrEmpty(card.ModImageDataUrl)) continue;
            var bytes = DataUrlToBytes(card.ModImageDataUrl);
            if (bytes is null) continue;
            File.WriteAllBytes(Path.Combine(dir, $"{card.CardId}.png"), bytes);
        }
    }

    /// <summary>
    /// Empacota os bytes grayscale 40×32 de cada carta num único arquivo
    /// <c>extracted/thumbnails.gray</c>. Sem isso, depois que o MRG é
    /// apagado, o detector OpenCV (template matching) fica sem
    /// <see cref="Card.ThumbnailPixels"/> e não acha carta nenhuma na
    /// captura da tela do emulador. ~924 KB total — irrelevante.
    ///
    /// Layout: 4 bytes header (count uint32 LE) + count × 1280 bytes.
    /// </summary>
    private static void WriteThumbnailGrays(string modFolder, IReadOnlyList<Card> cards)
    {
        var path = Path.Combine(modFolder, ThumbnailsFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Indexa por CardId pra recompor em ordem; cartas sem pixels
        // (raras) ficam preenchidas com zeros pra manter alinhamento.
        var byId = cards.Where(c => c.ThumbnailPixels is not null)
                        .ToDictionary(c => c.CardId, c => c.ThumbnailPixels!);
        if (byId.Count == 0) return;

        int maxId = byId.Keys.Max();
        using var fs = File.Create(path);
        Span<byte> header = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)maxId);
        fs.Write(header);

        var empty = new byte[ThumbnailBytes];
        for (int id = 1; id <= maxId; id++)
        {
            var bytes = byId.TryGetValue(id, out var b) && b.Length == ThumbnailBytes
                ? b
                : empty;
            fs.Write(bytes, 0, ThumbnailBytes);
        }
    }

    /// <summary>
    /// Grava todas as variantes de frame (10 cycles × 7 colors = 70 BMPs)
    /// em <c>extracted/frames/{cycle}_{color}.png</c>. Reusa o registry
    /// já populado pelo parser (em memória).
    /// </summary>
    private static void WriteFrameImages(string modFolder)
    {
        var dir = Path.Combine(modFolder, FramesDir);
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.EnumerateFiles(dir, "*.png")) File.Delete(f);

        for (int cy = 0; cy < CardFrameDecoder.Cycles.Length; cy++)
        for (int co = 0; co < 7; co++)
        {
            var url = CardFrameRegistry.GetFrame(cy, co);
            if (url is null) continue;
            var bytes = DataUrlToBytes(url);
            if (bytes is null) continue;
            File.WriteAllBytes(
                Path.Combine(dir, $"{cy}_{co}.png"), bytes);
        }
    }

    /// <summary>
    /// Extrai os bytes binários de uma data URL no formato
    /// <c>data:image/png;base64,XXXX</c>. Retorna null se não for esse formato.
    /// </summary>
    private static byte[]? DataUrlToBytes(string dataUrl)
    {
        var idx = dataUrl.IndexOf(',');
        if (idx < 0) return null;
        try { return Convert.FromBase64String(dataUrl[(idx + 1)..]); }
        catch { return null; }
    }

    /// <summary>
    /// Extrai os ícones de tipo (24), guardião (13) e atributo (9) do MRG
    /// e escreve em pastas separadas. Cria também as pastas
    /// <c>duelists/</c>, <c>names/</c> e <c>star/</c> vazias —
    /// reverse engineering desses sprites ainda é TODO de fase futura.
    /// </summary>
    private static void WriteSpriteImages(string modFolder, byte[] mrg, bool hasExtraDuelist)
    {
        // Os sprites estão organizados num sheet 128px de largura, com 8
        // sprites de 16×16 por linha. Tipos ocupam linhas 0-2 (24 sprites);
        // guardiões ocupam linhas 3-4 (13 sprites).
        WriteSpriteCategory(modFolder, TypesDir,
            mrg, TypesGuardiansBody,
            count:    TypesCount,
            startSpriteIndex: 0,
            clutForSprite: m => TypesGuardiansClut + 32 * m);

        WriteSpriteCategory(modFolder, GuardiansDir,
            mrg, TypesGuardiansBody,
            count:    GuardiansCount,
            startSpriteIndex: TypesCount,                         // sprite 24+
            clutForSprite: _ => TypesGuardiansClut + GuardiansSharedClutOff);

        // Attributes têm layout DIFERENTE de types/guardians: 9 sprites
        // numa única linha (x = 16*o, y = 0), o que estoura o limite
        // de 8/linha do layout genérico. O 9º atributo (o=8) cai em
        // x=128, y=0 — ainda dentro do sheet 256-wide.
        WriteAttributeSprites(modFolder, mrg);

        WriteStarSprite(modFolder, mrg);
        WriteDuelistPortraits(modFolder, mrg, hasExtraDuelist);
        WriteNameSprites(modFolder, mrg);
    }

    // ── Star (1 ícone 8×8 4bpp) ────────────────────────────────────────
    // Reverse-engineered do lab.js (função aDE):
    //   A4(8, q1(B.dE, oQ(p, 0xF02800, 32768), oQ(p, 0xB84180, 32), 128, !0, 256), 0, 16, 8)
    // Body é o sheet de atributos (0xF02800), CLUT em 0xB84180, posição (0,16), tamanho 8×8.
    private const int StarBody     = AttributesBody;
    private const int StarClut     = 0xB84180;
    private const int StarX        = 0;
    private const int StarY        = 16;
    private const int StarSize     = 8;

    private static void WriteStarSprite(string modFolder, byte[] mrg)
    {
        var dir = Path.Combine(modFolder, StarDir);
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.EnumerateFiles(dir, "*.png")) File.Delete(f);

        var bytes = SpriteDecoder.ExtractRect4bpp(
            mrg, StarBody, SpriteSheetWidth,
            StarX, StarY, StarSize, StarSize,
            StarClut, clutColors: 16);
        if (bytes is not null)
            File.WriteAllBytes(Path.Combine(dir, "0.png"), bytes);
    }

    // ── Duelists (39 ou 40 portraits 48×48 8bpp) ───────────────────────
    // Reverse-engineered do lab.js (função a9f):
    //   for each duelist d in 0..N-1:
    //     n = 0xF55EC0 + d * 2432  (- 2432 quando o MOD tem extra duelist)
    //     body = mrg[n .. n+2304]
    //     clut = mrg[n+2304 .. n+2432]   (= 64 cores RGB555)
    //     img  = 48×48 8bpp paletted
    //
    // Mods com "extra duelist" (ex.: Rakuza no TLM) têm 40 portraits
    // começando 1 slot ANTES do offset padrão — o que coloca Rakuza no
    // índice 0 e empurra os demais pra +1. O usuário marca essa flag
    // no cadastro do MOD; auto-detect via CLUT-likeness é frágil porque
    // o slot extra pode ter assinatura ambígua.
    private const int DuelistsBase   = 0xF55980;  // = 16_079_232 (não 0xF55EC0!)
    private const int DuelistSlot    = 2432;
    private const int DuelistImgSize = 48;
    private const int DuelistColors  = 64;

    private static void WriteDuelistPortraits(string modFolder, byte[] mrg, bool hasExtraDuelist)
    {
        var dir = Path.Combine(modFolder, DuelistsDir);
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.EnumerateFiles(dir, "*.png")) File.Delete(f);

        int baseOff = hasExtraDuelist ? DuelistsBase - DuelistSlot : DuelistsBase;
        int count   = hasExtraDuelist ? 40 : 39;

        for (int i = 0; i < count; i++)
        {
            int slotStart = baseOff + i * DuelistSlot;
            int bodyOff   = slotStart;
            int clutOff   = slotStart + 48 * 48;   // 2304

            var bytes = SpriteDecoder.Extract8bppPaletted(
                mrg, bodyOff, DuelistImgSize, DuelistImgSize,
                clutOff, DuelistColors);
            if (bytes is null) continue;
            File.WriteAllBytes(Path.Combine(dir, $"{i}.png"), bytes);
        }
    }

    // ── Names (722 imagens 96×14 4bpp, CLUT compartilhada) ─────────────
    // Reverse-engineered do lab.js (função a9e):
    //   for each card o in 0..721:
    //     slot_start = 0x168A00 + 14336 * o     (alinhado com card art slot)
    //     name_body  = slot_start + 9792 + 512  (= 0x16B240 + 14336*o)
    //     img        = 96×14 4bpp; CLUT compartilhada em 0xF079C0 (8 cores).
    //
    // Em coordenadas do nosso ROM padrão:
    //   thumbnails ficam em 0x16BAE0 + 14336*o; nome fica 0x8A0 antes
    //   (= 2208 bytes antes do thumbnail no mesmo slot).
    // Slot inicia em 0x169000 (= 1_478_656, mesmo offset do CardArt);
    // dentro do slot: art body (9792) + art clut (512) + NAME body (672).
    // Assim NamesBaseSlotStart = 0x169000 + 10304 = 0x16B840 (NÃO 0x16B240).
    private const int NamesBaseSlotStart = 0x16B840;   // = 0x169000 + 10304
    private const int NamesStride        = 14336;
    private const int NamesClut          = 0xF079C0;
    private const int NameWidth          = 96;
    private const int NameHeight         = 14;
    private const int NameBytes          = NameWidth * NameHeight / 2;  // 672

    private static void WriteNameSprites(string modFolder, byte[] mrg)
    {
        var dir = Path.Combine(modFolder, NamesDir);
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.EnumerateFiles(dir, "*.png")) File.Delete(f);

        for (int o = 0; o < 722; o++)
        {
            int bodyOff = NamesBaseSlotStart + o * NamesStride;
            if (bodyOff + NameBytes > mrg.Length) break;

            // O 4bpp decoder espera "sheet width" pra calcular bytes/row
            // = sheet width / 2. Aqui o "sheet" é a própria largura do nome.
            var bytes = SpriteDecoder.ExtractRect4bpp(
                mrg, bodyOff, sheetWidth: NameWidth,
                x: 0, y: 0, NameWidth, NameHeight,
                NamesClut, clutColors: 8);
            if (bytes is null) continue;
            File.WriteAllBytes(Path.Combine(dir, $"{o}.png"), bytes);
        }
    }

    /// <summary>
    /// Layout dos atributos no sheet 0xF02800: linha única, 9 sprites
    /// 16×16 lado a lado em (16*o, 0). Como o sheet é 256 wide, x vai
    /// até 128 (último sprite ocupa cols 128..143). CLUT por atributo.
    /// </summary>
    private static void WriteAttributeSprites(string modFolder, byte[] mrg)
    {
        var dir = Path.Combine(modFolder, AttributesDir);
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.EnumerateFiles(dir, "*.png")) File.Delete(f);

        for (int o = 0; o < AttributesCount; o++)
        {
            int x = 16 * o;
            int y = 0;
            int clutOff = AttributesClut + 32 * o;
            var bytes = SpriteDecoder.ExtractSprite(
                mrg, AttributesBody, SpriteSheetWidth,
                x, y, SpriteSize, clutOff);
            if (bytes is null) continue;
            File.WriteAllBytes(Path.Combine(dir, $"{o}.png"), bytes);
        }
    }

    /// <summary>Escreve N sprites consecutivos do sheet, calculando posição
    /// (x, y) baseada no índice. Layout: 8 sprites por linha, 16×16 cada.
    ///
    /// Para a categoria <c>guardians</c> os arquivos são nomeados a partir
    /// de 1 (1.png, 2.png, ...) pra casar com o ID do guardian star usado
    /// no modelo (0 = None, 1 = Mars, ...). Demais categorias mantêm
    /// nomenclatura 0-based.</summary>
    private static void WriteSpriteCategory(
        string modFolder, string subdir, byte[] mrg, int bodyOffset,
        int count, int startSpriteIndex, Func<int, int> clutForSprite)
    {
        var dir = Path.Combine(modFolder, subdir);
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.EnumerateFiles(dir, "*.png")) File.Delete(f);

        bool isGuardians = subdir == GuardiansDir;

        for (int i = 0; i < count; i++)
        {
            int absoluteIndex = startSpriteIndex + i;
            int x = (absoluteIndex % 8) * SpriteSize;
            int y = (absoluteIndex / 8) * SpriteSize;
            // O índice do CLUT pode usar o relativo (i) ou o absoluto —
            // depende da categoria. Passamos a função pro caller decidir.
            int clutIdxArg = isGuardians ? absoluteIndex : i;
            int clutOff = clutForSprite(clutIdxArg);

            var bytes = SpriteDecoder.ExtractSprite(
                mrg, bodyOffset, SpriteSheetWidth,
                x, y, SpriteSize, clutOff);
            if (bytes is null) continue;

            int fileIndex = isGuardians ? i + 1 : i;
            File.WriteAllBytes(Path.Combine(dir, $"{fileIndex}.png"), bytes);
        }
    }
}
