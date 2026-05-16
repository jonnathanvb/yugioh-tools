using yugiho_tools.Application.Helpers;
using yugiho_tools.Application.UseCases;
using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;
using yugiho_tools.Infrastructure.Storage;

namespace yugiho_tools.Application.Services;

/// <summary>
/// Lê <c>MODs/{slug}/data.json</c> + pastas de assets (cards, frames,
/// attributes, duelists, guardians, star, types) e reidrata um
/// <see cref="LoadedRomData"/> pronto pra consumo na UI.
///
/// Estrutura esperada (formato distribuído via ZIP do catálogo):
/// <code>
/// MODs/{slug}/
///   data.json
///   cards/      attributes/  duelists/  frames/
///   guardians/  star/        types/
/// </code>
/// A pasta <c>names/</c> não é mais consumida (foi removida do pipeline).
/// </summary>
public class ExtractedDataLoader
{
    // Subpastas dentro da raiz do mod importado.
    public const string CardsDir      = "cards";
    public const string FramesDir     = "frames";
    public const string TypesDir      = "types";
    public const string GuardiansDir  = "guardians";
    public const string AttributesDir = "attributes";
    public const string DuelistsDir   = "duelists";
    public const string StarDir       = "star";

    public const string ThumbnailsFile = "thumbnails.gray";

    private readonly IModRepository          _repo;
    private readonly ExtractedDataRepository _store;
    private readonly SharedImagesService?    _sharedImages;

    public ExtractedDataLoader(
        IModRepository repo,
        ExtractedDataRepository store,
        SharedImagesService? sharedImages = null)
    {
        _repo         = repo;
        _store        = store;
        _sharedImages = sharedImages;
    }

    public bool HasExtractedData(Mod mod)
        => _store.Exists(_repo.GetModFolderPath(mod));

    /// <summary>Mensagem + percentual reportados durante o carregamento;
    /// a UI mostra na barra global pra usuário ver onde o load está.</summary>
    public record LoadProgress(int Percent, string Message);

    public async Task<LoadedRomData?> TryLoadAsync(
        Mod mod,
        IProgress<LoadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var folder = _repo.GetModFolderPath(mod);

        progress?.Report(new(5, "Lendo data.json…"));
        var data = await _store.ReadAsync(folder);
        if (data is null) return null;
        ct.ThrowIfCancellationRequested();

        progress?.Report(new(15, $"Convertendo {data.Cards.Count} cartas…"));
        var cards = data.Cards.Select(ToCardEntity).ToList();
        var duelists = data.Duelists.Select(d => new Duelist
        {
            Id     = d.Id,
            Name   = d.Name,
            Deck   = d.Deck.Length   == 722 ? d.Deck   : new ushort[722],
            SaPow  = d.SaPow.Length  == 722 ? d.SaPow  : new ushort[722],
            BcdPow = d.BcdPow.Length == 722 ? d.BcdPow : new ushort[722],
            SaTec  = d.SaTec.Length  == 722 ? d.SaTec  : new ushort[722],
        }).ToList();
        ct.ThrowIfCancellationRequested();

        // Loading de imagens é a parte mais demorada (centenas de file
        // reads + base64). Reporta progresso fino pra UI mostrar avanço.
        await LoadCardImagesFromDiskAsync(folder, cards, mod,
            // Mapeia 20..75% do progresso global pra essa fase.
            new Progress<LoadProgress>(p =>
                progress?.Report(new(20 + (int)(55.0 * p.Percent / 100), p.Message))),
            ct);

        progress?.Report(new(78, "Lendo thumbnails…"));
        LoadThumbnailGraysFromDisk(folder, cards);

        progress?.Report(new(82, "Lendo frames…"));
        LoadFramesFromDisk(folder);

        progress?.Report(new(90, "Lendo atributos/tipos/estrelas…"));
        LoadExtractedAssetsFromDisk(folder);
        _sharedImages?.Apply();

        progress?.Report(new(95, "Indexando fusões…"));
        CardFrameRegistry.LoadPositions(data.Positions);
        CardFrameRegistry.LoadFusionResults(cards);

        progress?.Report(new(100, "Pronto."));
        return new LoadedRomData(cards, duelists, data.Positions);
    }

    /// <summary>Resolve um subdir tentando primeiro o formato novo
    /// (flat) e caindo no legado <c>extracted/{name}</c> só se existir,
    /// pra não quebrar mods já instalados via fluxo antigo durante a
    /// transição.</summary>
    private static string ResolveAssetDir(string modFolder, string name)
    {
        var flat = Path.Combine(modFolder, name);
        if (Directory.Exists(flat)) return flat;
        var legacy = Path.Combine(modFolder, "extracted", name);
        return Directory.Exists(legacy) ? legacy : flat;
    }

    private static string ResolveAssetFile(string modFolder, string name)
    {
        var flat = Path.Combine(modFolder, name);
        if (File.Exists(flat)) return flat;
        var legacy = Path.Combine(modFolder, "extracted", name);
        return File.Exists(legacy) ? legacy : flat;
    }

    private static async Task LoadCardImagesFromDiskAsync(
        string modFolder, List<Card> cards, Mod mod,
        IProgress<LoadProgress>? progress,
        CancellationToken ct)
    {
        var cardsRoot = ResolveAssetDir(modFolder, CardsDir);
        if (!Directory.Exists(cardsRoot)) return;

        // Layout novo: cards/{variant}/{id}.{png|jpg}, com variantes
        // sd / hd / mini_sd / mini_hd. Layout legado (mods antigos):
        // cards/{id}.png direto na raiz — detectado pra continuar funcionando.
        var hasLegacyFlat = Directory.EnumerateFiles(cardsRoot, "*.png").Any();

        if (hasLegacyFlat)
        {
            await LoadFlatCardsLegacyAsync(cardsRoot, cards, progress, ct);
        }
        else
        {
            // Variante principal escolhida pelo usuário, com fallback pra
            // SD (sempre presente no pacote) caso a configurada não exista.
            var mainVariant = ResolveExistingVariant(
                cardsRoot, mod.CardImageVariant, CardVariants.Sd);
            if (mainVariant is not null)
                await LoadVariantIntoAsync(cardsRoot, mainVariant, cards,
                    (c, url) => c.ModImageDataUrl = url,
                    // 0..70% da fase: imagem principal é o maior custo.
                    new Progress<LoadProgress>(p =>
                        progress?.Report(new(
                            (int)(70.0 * p.Percent / 100),
                            $"Imagens {mainVariant} ({p.Message})"))),
                    ct);

            // Mini pro grafo de fusão — separada pra que o usuário possa
            // ter HD na vista principal e mini_sd compacto no grafo.
            var miniVariant = ResolveExistingVariant(
                cardsRoot, mod.FusionMiniVariant, CardVariants.MiniSd);
            if (miniVariant is not null)
                await LoadVariantIntoAsync(cardsRoot, miniVariant, cards,
                    (c, url) => c.MiniImageDataUrl = url,
                    // 70..100% — mini é mais leve.
                    new Progress<LoadProgress>(p =>
                        progress?.Report(new(
                            70 + (int)(30.0 * p.Percent / 100),
                            $"Imagens {miniVariant} ({p.Message})"))),
                    ct);
        }

        CardImage.LoadFromCards(cards);
    }

    /// <summary>Compat: layout antigo onde <c>cards/{id}.png</c> ficavam
    /// na raiz. Mantido pra não quebrar mods já importados.</summary>
    private static async Task LoadFlatCardsLegacyAsync(
        string cardsDir, List<Card> cards,
        IProgress<LoadProgress>? progress, CancellationToken ct)
    {
        int done = 0, total = cards.Count;
        foreach (var c in cards)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(cardsDir, $"{c.CardId}.png");
            if (File.Exists(path))
            {
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    c.ModImageDataUrl =
                        $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
                }
                catch { /* skip — cai pro template */ }
            }
            done++;
            if (done % 50 == 0)
            {
                await Task.Yield();
                progress?.Report(new(
                    (int)(100.0 * done / total), $"{done}/{total}"));
            }
        }
    }

    /// <summary>Devolve <paramref name="preferred"/> se a subpasta existir,
    /// senão <paramref name="fallback"/> se existir, senão null.</summary>
    private static string? ResolveExistingVariant(
        string cardsDir, string? preferred, string fallback)
    {
        if (!string.IsNullOrEmpty(preferred) &&
            Directory.Exists(Path.Combine(cardsDir, preferred)))
            return preferred;
        if (Directory.Exists(Path.Combine(cardsDir, fallback)))
            return fallback;
        return null;
    }

    /// <summary>Lê todos os arquivos da variante (png ou jpg) e grava o
    /// data URL na carta correspondente via <paramref name="setter"/>.
    /// Suporta png e jpg porque <c>sd</c> usa JPEG (menor) e as outras
    /// variantes PNG. Usa <see cref="File.ReadAllBytes"/> sync (mais
    /// previsível no Mac Catalyst que o async) e dá <see cref="Task.Yield"/>
    /// a cada 32 cartas pra liberar o thread pool e cancelamento.</summary>
    private static async Task LoadVariantIntoAsync(
        string cardsDir, string variant, List<Card> cards,
        Action<Card, string> setter,
        IProgress<LoadProgress>? progress,
        CancellationToken ct)
    {
        var dir = Path.Combine(cardsDir, variant);
        if (!Directory.Exists(dir)) return;
        int done = 0, total = cards.Count, errors = 0;
        string? lastError = null;
        foreach (var c in cards)
        {
            ct.ThrowIfCancellationRequested();
            var (path, mime) = FindCardFile(dir, c.CardId);
            if (path is not null)
            {
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    setter(c, $"data:{mime};base64,{Convert.ToBase64String(bytes)}");
                }
                catch (Exception ex)
                {
                    // Conta erros e guarda último pra reportar na barra
                    // de progresso — antes eram silenciosamente engolidos
                    // e o usuário só via "ficou parado".
                    errors++;
                    lastError = $"{Path.GetFileName(path)}: {ex.GetType().Name}";
                }
            }
            done++;
            if (done % 50 == 0)
            {
                // Libera thread pool periodicamente — sem isso, 700+ reads
                // sync seguidos podem inanir outras tasks (incluindo o
                // próprio handler de Progress que faz dispatch pra UI).
                await Task.Yield();
                var msg = errors > 0
                    ? $"{done}/{total} ({errors} erros · {lastError})"
                    : $"{done}/{total}";
                progress?.Report(new((int)(100.0 * done / total), msg));
            }
        }
        // Reporta total final mesmo se não bater num múltiplo de 50.
        if (errors > 0)
            progress?.Report(new(100,
                $"{done}/{total} ({errors} erros · último: {lastError})"));
    }

    private static (string? Path, string Mime) FindCardFile(string dir, int cardId)
    {
        var png = Path.Combine(dir, $"{cardId}.png");
        if (File.Exists(png)) return (png, "image/png");
        var jpg = Path.Combine(dir, $"{cardId}.jpg");
        if (File.Exists(jpg)) return (jpg, "image/jpeg");
        var jpeg = Path.Combine(dir, $"{cardId}.jpeg");
        if (File.Exists(jpeg)) return (jpeg, "image/jpeg");
        return (null, "");
    }

    /// <summary>Lista as variantes (subpastas) disponíveis em
    /// <c>cards/</c> de um MOD já importado. Usado pela UI de configuração
    /// pra mostrar só opções que de fato existem no pacote.</summary>
    public static IReadOnlyList<string> GetAvailableCardVariants(string modFolder)
    {
        var cardsRoot = ResolveAssetDir(modFolder, CardsDir);
        if (!Directory.Exists(cardsRoot)) return [];
        try
        {
            return Directory.EnumerateDirectories(cardsRoot)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray()!;
        }
        catch { return []; }
    }

    /// <summary>Carrega bytes da PRIMEIRA carta da variante pra usar como
    /// preview na tela de configuração. Devolve data URL ou null. Procura
    /// IDs 1..3 pra evitar pegar arquivo "0" se o pacote começar do 1.</summary>
    public static string? GetVariantPreview(string modFolder, string variant)
    {
        var cardsRoot = ResolveAssetDir(modFolder, CardsDir);
        var dir = Path.Combine(cardsRoot, variant);
        if (!Directory.Exists(dir)) return null;
        for (int id = 1; id <= 3; id++)
        {
            var (path, mime) = FindCardFile(dir, id);
            if (path is null) continue;
            try
            {
                var bytes = File.ReadAllBytes(path);
                return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
            }
            catch { /* tenta próximo id */ }
        }
        return null;
    }

    private static void LoadThumbnailGraysFromDisk(string modFolder, List<Card> cards)
    {
        const int thumbBytes = 40 * 32;
        var path = ResolveAssetFile(modFolder, ThumbnailsFile);
        if (!File.Exists(path)) return;
        try
        {
            var raw = File.ReadAllBytes(path);
            if (raw.Length < 4) return;
            int count = (int)System.Buffers.Binary.BinaryPrimitives
                            .ReadUInt32LittleEndian(raw.AsSpan(0, 4));
            int expected = 4 + count * thumbBytes;
            if (raw.Length < expected) return;

            foreach (var c in cards)
            {
                if (c.CardId < 1 || c.CardId > count) continue;
                int off = 4 + (c.CardId - 1) * thumbBytes;
                var slice = new byte[thumbBytes];
                Array.Copy(raw, off, slice, 0, thumbBytes);
                c.ThumbnailPixels = slice;
            }
        }
        catch { /* thumbnails ausentes → detect falha mas app não quebra */ }
    }

    private static void LoadFramesFromDisk(string modFolder)
    {
        var dir = ResolveAssetDir(modFolder, FramesDir);
        if (!Directory.Exists(dir)) return;
        var dict = new Dictionary<(int, int), string>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.png"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var parts = name.Split('_');
            if (parts.Length != 2) continue;
            if (!int.TryParse(parts[0], out var cy)) continue;
            if (!int.TryParse(parts[1], out var co)) continue;
            try
            {
                var bytes = File.ReadAllBytes(path);
                dict[(cy, co)] =
                    $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
            }
            catch { /* skip */ }
        }
        CardFrameRegistry.LoadFromMemory(dict);
    }

    private static void LoadExtractedAssetsFromDisk(string modFolder)
    {
        ExtractedAssets.Reset();

        var attrDir = ResolveAssetDir(modFolder, AttributesDir);
        if (Directory.Exists(attrDir))
        {
            foreach (var path in Directory.EnumerateFiles(attrDir, "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!int.TryParse(name, out var idx)) continue;
                var url = ReadAsDataUrl(path);
                if (url is not null) ExtractedAssets.SetAttribute(idx, url);
            }
        }

        var starPath = Path.Combine(ResolveAssetDir(modFolder, StarDir), "0.png");
        if (File.Exists(starPath))
        {
            var url = ReadAsDataUrl(starPath);
            if (url is not null) ExtractedAssets.SetStar(url);
        }

        var duelistDir = ResolveAssetDir(modFolder, DuelistsDir);
        if (Directory.Exists(duelistDir))
        {
            foreach (var path in Directory.EnumerateFiles(duelistDir, "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!int.TryParse(name, out var idx)) continue;
                var url = ReadAsDataUrl(path);
                if (url is not null) ExtractedAssets.SetDuelist(idx, url);
            }
        }

        var typesDir = ResolveAssetDir(modFolder, TypesDir);
        if (Directory.Exists(typesDir))
        {
            foreach (var path in Directory.EnumerateFiles(typesDir, "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!int.TryParse(name, out var idx)) continue;
                var url = ReadAsDataUrl(path);
                if (url is not null) ExtractedAssets.SetType(idx, url);
            }
        }

        var guardiansDir = ResolveAssetDir(modFolder, GuardiansDir);
        if (Directory.Exists(guardiansDir))
        {
            foreach (var path in Directory.EnumerateFiles(guardiansDir, "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!int.TryParse(name, out var idx)) continue;
                var url = ReadAsDataUrl(path);
                if (url is not null) ExtractedAssets.SetGuardian(idx, url);
            }
        }
    }

    private static string? ReadAsDataUrl(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
        }
        catch { return null; }
    }

    private static Card ToCardEntity(ExtractedCard c) => new Card
    {
        CardId         = c.Id,
        Name           = c.Name,
        Description    = c.Description,
        Attack         = c.Atk,
        Defense        = c.Def,
        Level          = c.Lvl,
        CardType       = c.Type,
        Attribute      = c.Attribute,
        GuardianStar1  = c.Guardian1,
        GuardianStar2  = c.Guardian2,
        Equips         = new List<int>(c.Equips),
        EquipTargets   = new List<int>(c.EquipTargets),
        // FusionEngine indexa por (handCard, myCard) usando 0-based; aqui
        // convertemos do schema novo ($1=mat1, $2=mat2, $3=result, todos
        // 1-based) pra perspectiva da carta atual: o "outro material" é
        // o que NÃO é ela. Quando $3==self, esta carta é o RESULTADO da
        // fusão (não material) — pula pra não poluir FusionMaterials.
        FusionMaterials = c.Fusions
            .Where(f => f.Mat1 == c.Id || f.Mat2 == c.Id)
            .Select(f => (f.Mat1 == c.Id ? f.Mat2 : f.Mat1) - 1)
            .ToList(),
        FusionResults = c.Fusions
            .Where(f => f.Mat1 == c.Id || f.Mat2 == c.Id)
            .Select(f => f.Result - 1)
            .ToList(),
        IsRitual = c.IsRitual,
        IsFusion = c.IsFusion,
        Limited        = c.Limited,
        Password       = c.Password,
        CostStars      = c.CostStars,
        Rituals        = c.Rituals.Select(r => new RitualRecipe
                         {
                             Ingredients = new List<int>(r.Ingredients),
                             Result      = r.Result,
                         }).ToList(),
        DescriptionsByLanguage = new Dictionary<string, string>(c.DescriptionsByLanguage),
    };
}
