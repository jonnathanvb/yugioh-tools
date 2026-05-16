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
                    new Progress<LoadProgress>(p =>
                        progress?.Report(new(
                            p.Percent,
                            $"Imagens {mainVariant} ({p.Message})"))),
                    ct);
        }

        CardImage.LoadFromCards(cards);
    }

    /// <summary>Compat: layout antigo onde <c>cards/{id}.png</c> ficavam
    /// na raiz. Mantido pra não quebrar mods já importados. Também
    /// passou pra URL <c>modimg://</c> em vez de inline base64.</summary>
    private static async Task LoadFlatCardsLegacyAsync(
        string cardsDir, List<Card> cards,
        IProgress<LoadProgress>? progress, CancellationToken ct)
    {
        var relBase = GetAppDataRelativePath(cardsDir);
        int done = 0, total = cards.Count;
        foreach (var c in cards)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(cardsDir, $"{c.CardId}.png");
            if (File.Exists(path))
                c.ModImageDataUrl = AppDataUrl.For($"{relBase}/{c.CardId}.png");
            done++;
            if (done % 100 == 0)
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

    /// <summary>Resolve uma URL <c>modimg://</c> pra cada carta da variante,
    /// SEM ler bytes — o scheme handler do <see cref="WebKit.WKWebView"/>
    /// (no MacCatalyst) ou o handler do WebView2 (Windows) lê on-demand
    /// quando o navegador faz GET na URL.
    ///
    /// <para>Por que não <c>data:image/...;base64,...</c>? O BlazorWebView
    /// serializa o RenderBatch como UMA string JSON pelo IPC. Com 50 cartas
    /// HD inline (380 KB cada em base64), passa de 200 MB e estoura o
    /// limite de 256 MB do System.Text.Json. URLs <c>modimg://</c> só têm
    /// ~80 bytes cada, então o HTML fica pequeno e o WebView busca o
    /// binário em paralelo via handler de scheme.</para></summary>
    private static async Task LoadVariantIntoAsync(
        string cardsDir, string variant, List<Card> cards,
        Action<Card, string> setter,
        IProgress<LoadProgress>? progress,
        CancellationToken ct)
    {
        var dir = Path.Combine(cardsDir, variant);
        if (!Directory.Exists(dir)) return;

        // Caminho relativo ao AppDataDirectory pra montar a URL do scheme.
        // Tudo abaixo de AppDataDirectory mapeia 1:1 pra modimg:///{path}.
        var relBase = GetAppDataRelativePath(dir);

        int done = 0, total = cards.Count, errors = 0;
        string? lastError = null;
        foreach (var c in cards)
        {
            ct.ThrowIfCancellationRequested();
            var (path, _) = FindCardFile(dir, c.CardId);
            if (path is not null)
            {
                try
                {
                    // Só monta a URL — File.Exists já confirmou em FindCardFile.
                    var fileName = Path.GetFileName(path);
                    setter(c, AppDataUrl.For($"{relBase}/{fileName}"));
                }
                catch (Exception ex)
                {
                    errors++;
                    lastError = $"{Path.GetFileName(path)}: {ex.GetType().Name}";
                }
            }
            done++;
            if (done % 100 == 0)
            {
                await Task.Yield();
                var msg = errors > 0
                    ? $"{done}/{total} ({errors} erros · {lastError})"
                    : $"{done}/{total}";
                progress?.Report(new((int)(100.0 * done / total), msg));
            }
        }
        if (errors > 0)
            progress?.Report(new(100,
                $"{done}/{total} ({errors} erros · último: {lastError})"));
    }

    /// <summary>Converte um path absoluto pra rota relativa a partir do
    /// <see cref="FileSystem.AppDataDirectory"/>, com separadores '/'
    /// (consumível como URL). Usado pra montar URLs <c>modimg://</c>.</summary>
    private static string GetAppDataRelativePath(string absPath)
    {
        var root = FileSystem.AppDataDirectory;
        var full = Path.GetFullPath(absPath);
        if (!full.StartsWith(root, StringComparison.Ordinal))
            return full.Replace(Path.DirectorySeparatorChar, '/');
        return full[root.Length..]
            .TrimStart(Path.DirectorySeparatorChar)
            .Replace(Path.DirectorySeparatorChar, '/');
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
        var relBase = GetAppDataRelativePath(dir);
        var dict = new Dictionary<(int, int), string>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.png"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var parts = name.Split('_');
            if (parts.Length != 2) continue;
            if (!int.TryParse(parts[0], out var cy)) continue;
            if (!int.TryParse(parts[1], out var co)) continue;
            dict[(cy, co)] = AppDataUrl.For($"{relBase}/{name}.png");
        }
        CardFrameRegistry.LoadFromMemory(dict);
    }

    private static void LoadExtractedAssetsFromDisk(string modFolder)
    {
        ExtractedAssets.Reset();

        // Todos os assets agora viram URLs modimg:// — apenas verificam
        // existência do arquivo e armazenam o caminho, sem inline base64.
        LoadIndexedAsModimgUrls(modFolder, AttributesDir, ExtractedAssets.SetAttribute);
        LoadIndexedAsModimgUrls(modFolder, DuelistsDir,   ExtractedAssets.SetDuelist);
        LoadIndexedAsModimgUrls(modFolder, TypesDir,      ExtractedAssets.SetType);
        LoadIndexedAsModimgUrls(modFolder, GuardiansDir,  ExtractedAssets.SetGuardian);

        var starPath = Path.Combine(ResolveAssetDir(modFolder, StarDir), "0.png");
        if (File.Exists(starPath))
        {
            var rel = GetAppDataRelativePath(starPath);
            ExtractedAssets.SetStar(AppDataUrl.For(rel));
        }

    }

    /// <summary>Itera <c>{id}.png</c> dentro de <c>{modFolder}/{subdir}</c>
    /// e popula <see cref="ExtractedAssets"/> via <paramref name="setter"/>
    /// usando URLs <c>modimg://</c>. Não inline base64 — o
    /// <see cref="WebKit.WKWebView"/> resolve sob demanda.</summary>
    private static void LoadIndexedAsModimgUrls(
        string modFolder, string subdir, Action<int, string> setter)
    {
        var dir = ResolveAssetDir(modFolder, subdir);
        if (!Directory.Exists(dir)) return;
        var relBase = GetAppDataRelativePath(dir);
        foreach (var path in Directory.EnumerateFiles(dir, "*.png"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!int.TryParse(name, out var idx)) continue;
            setter(idx, AppDataUrl.For($"{relBase}/{name}.png"));
        }
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
