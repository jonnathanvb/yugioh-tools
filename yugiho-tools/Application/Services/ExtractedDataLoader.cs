using yugiho_tools.Application.Helpers;
using yugiho_tools.Application.UseCases;
using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;
using yugiho_tools.Infrastructure.Storage;

namespace yugiho_tools.Application.Services;

/// <summary>
/// Lê <c>MOD/{slug}/data.json</c> e reidrata um <see cref="LoadedRomData"/>
/// — versão "atalho" do <c>LoadRomDataUseCase</c> que substitui o parse
/// binário pesado quando o JSON pré-extraído está disponível. Usado pelo
/// <see cref="LoadedRomCache"/> antes de cair no parser.
/// </summary>
public class ExtractedDataLoader
{
    private readonly IModRepository          _repo;
    private readonly ExtractedDataRepository _store;

    public ExtractedDataLoader(IModRepository repo, ExtractedDataRepository store)
    {
        _repo  = repo;
        _store = store;
    }

    public bool HasExtractedData(Mod mod)
        => _store.Exists(_repo.GetModFolderPath(mod));

    /// <summary>
    /// Lê o JSON e reconstrói o <see cref="LoadedRomData"/> em memória.
    /// Retorna null se o JSON não existe ou está corrompido — caller
    /// cai no parser binário.
    /// </summary>
    public async Task<LoadedRomData?> TryLoadAsync(Mod mod)
    {
        var folder = _repo.GetModFolderPath(mod);
        var data = await _store.ReadAsync(folder);
        if (data is null) return null;

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

        // Popula os registries estáticos lendo os BMPs do disco que o
        // ModExtractor escreveu. Como são data URLs em base64 (decodadas
        // do disco), o cardCover renderiza o frame + arte normalmente.
        LoadCardImagesFromDisk(folder, cards);
        // Bytes grayscale 40×32 — usados pelo OpenCV detector. Sem
        // isso, o "Escanear emulador" na home não acha nenhuma carta
        // (todos os ThumbnailPixels nulos depois que o MRG sumiu).
        LoadThumbnailGraysFromDisk(folder, cards);
        LoadFramesFromDisk(folder);
        LoadExtractedAssetsFromDisk(folder);
        // Posições do frame (ATK/DEF/nome/atributo/estrelas) agora vêm
        // do JSON — sem dependência do SLUS no runtime.
        CardFrameRegistry.LoadPositions(data.Positions);
        CardFrameRegistry.LoadFusionResults(cards);

        return new LoadedRomData(cards, duelists, data.Positions);
    }

    /// <summary>
    /// Lê os BMPs em <c>extracted/cards/</c> e popula
    /// <see cref="CardImage.LoadFromCards"/> com data URLs montadas
    /// localmente. Se algum BMP estiver faltando, a carta cai no
    /// fallback TEA via <c>CardImage.Url</c>.
    /// </summary>
    private static void LoadCardImagesFromDisk(string modFolder, List<Card> cards)
    {
        var dir = Path.Combine(modFolder, ModExtractor.CardsDir);
        if (!Directory.Exists(dir)) return;
        foreach (var c in cards)
        {
            var path = Path.Combine(dir, $"{c.CardId}.png");
            if (!File.Exists(path)) continue;
            try
            {
                var bytes = File.ReadAllBytes(path);
                c.ModImageDataUrl =
                    $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
            }
            catch { /* ignora — carta cai pra TEA */ }
        }
        CardImage.LoadFromCards(cards);
    }

    /// <summary>
    /// Lê <c>extracted/thumbnails.gray</c> e popula
    /// <see cref="Card.ThumbnailPixels"/> de cada carta. Sem isso o
    /// detector OpenCV não tem template pra match e a função
    /// "Escanear emulador" na home não acha nenhuma carta — porque o
    /// MRG (fonte original dos pixels) é apagado depois da extração.
    /// </summary>
    private static void LoadThumbnailGraysFromDisk(string modFolder, List<Card> cards)
    {
        const int thumbBytes = 40 * 32;
        var path = Path.Combine(modFolder, ModExtractor.ThumbnailsFile);
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
        catch { /* arquivo corrompido; cartas ficam sem template — detect falha mas app não quebra */ }
    }

    /// <summary>
    /// Lê os 70 BMPs de moldura em <c>extracted/frames/</c> e popula o
    /// <see cref="CardFrameRegistry"/>. Filename: <c>{cycle}_{color}.png</c>.
    /// </summary>
    private static void LoadFramesFromDisk(string modFolder)
    {
        var dir = Path.Combine(modFolder, ModExtractor.FramesDir);
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

    /// <summary>
    /// Lê <c>extracted/names/{cardId-1}.png</c>, <c>extracted/attributes/{attrId}.png</c>
    /// e <c>extracted/star/0.png</c>, popula o <see cref="ExtractedAssets"/>.
    /// Imagens viram data URLs base64 — UI usa direto em &lt;img src&gt;.
    /// </summary>
    private static void LoadExtractedAssetsFromDisk(string modFolder)
    {
        ExtractedAssets.Reset();

        // Nomes: arquivo o.png corresponde a card index o (0-based);
        // CardId é 1-based, então armazenamos com cardId = o + 1.
        var namesDir = Path.Combine(modFolder, "extracted/names");
        if (Directory.Exists(namesDir))
        {
            foreach (var path in Directory.EnumerateFiles(namesDir, "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!int.TryParse(name, out var idx)) continue;
                var url = ReadAsDataUrl(path);
                if (url is not null) ExtractedAssets.SetName(idx + 1, url);
            }
        }

        // Atributos
        var attrDir = Path.Combine(modFolder, "extracted/attributes");
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

        // Star (único arquivo)
        var starPath = Path.Combine(modFolder, "extracted/star/0.png");
        if (File.Exists(starPath))
        {
            var url = ReadAsDataUrl(starPath);
            if (url is not null) ExtractedAssets.SetStar(url);
        }

        // Duelistas (39 ou 40 portraits)
        var duelistDir = Path.Combine(modFolder, "extracted/duelists");
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

        // Tipos (24 sprites 16×16) — usados tanto no detalhe da carta
        // quanto inline na descrição (marcador <_N_>).
        var typesDir = Path.Combine(modFolder, "extracted/types");
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

        // Guardians (sprites 16×16). Os arquivos são nomeados 1..N para
        // alinhar com o ID do guardian star (0 = None, sem ícone).
        var guardiansDir = Path.Combine(modFolder, "extracted/guardians");
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
        FusionMaterials = c.Fusions.Select(f => f.Material).ToList(),
        FusionResults   = c.Fusions.Select(f => f.Result).ToList(),
        IsRitual       = c.IsRitual,
        // IsFusion: lab pode ou não trazer; se não trouxe, deriva da
        // própria existência de fusões em que é resultado (FusionResults
        // popula isso quando temos a tabela completa).
        IsFusion       = c.IsFusion || c.Fusions.Any(f => f.Result == c.Id - 1),
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
