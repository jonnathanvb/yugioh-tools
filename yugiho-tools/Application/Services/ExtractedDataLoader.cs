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

    public ExtractedDataLoader(IModRepository repo, ExtractedDataRepository store)
    {
        _repo  = repo;
        _store = store;
    }

    public bool HasExtractedData(Mod mod)
        => _store.Exists(_repo.GetModFolderPath(mod));

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

        LoadCardImagesFromDisk(folder, cards);
        LoadThumbnailGraysFromDisk(folder, cards);
        LoadFramesFromDisk(folder);
        LoadExtractedAssetsFromDisk(folder);
        CardFrameRegistry.LoadPositions(data.Positions);
        CardFrameRegistry.LoadFusionResults(cards);

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

    private static void LoadCardImagesFromDisk(string modFolder, List<Card> cards)
    {
        var dir = ResolveAssetDir(modFolder, CardsDir);
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
        FusionMaterials = c.Fusions.Select(f => f.Material).ToList(),
        FusionResults   = c.Fusions.Select(f => f.Result).ToList(),
        IsRitual       = c.IsRitual,
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
