namespace yugiho_tools.Domain.Entities;

public class Card()
{
    public int CardId { get; set; }
    public string Name { get; set; } = "";
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int GuardianStar1 { get; set; }
    public int GuardianStar2 { get; set; }
    public int CardType { get; set; }
    public int Level { get; set; }
    public int Attribute { get; set; }
    public string Description { get; set; } = "";


    public List<int> FusionMaterials { get; set; } = [];
    public List<int> FusionResults { get; set; } = [];

    /// <summary>
    /// Compatible equip cards (card IDs, 0-based). Populated when this card is a
    /// monster: lists which equip-magic cards can target it. Populated by RomParser.
    /// </summary>
    public List<int> Equips { get; set; } = [];

    /// <summary>
    /// Monsters this card can equip onto (card IDs, 0-based). Populated when this
    /// card is an equip: inverse of <see cref="Equips"/>. Computed by RomParser.
    /// </summary>
    public List<int> EquipTargets { get; set; } = [];


    /// <summary>True quando esta carta é resultado de um ritual.</summary>
    public bool IsRitual { get; set; }
    /// <summary>True quando esta carta tem fusões registradas (= aparece
    /// como result em alguma fusão). Calculado, não lido do JSON.</summary>
    public bool IsFusion { get; set; }
    /// <summary>0 = livre. 1-3 = limite (por slot no deck).</summary>
    public int Limited { get; set; }

    /// <summary>Password da carta (8 dígitos, formato YGO TCG).</summary>
    public string Password { get; set; } = "";
    /// <summary>Custo em estrelas (Star Chips) na loja.</summary>
    public int CostStars { get; set; }

    /// <summary>Combinações de ingredientes que invocam esta carta como
    /// ritual. Cada item lista os card IDs (0-based) que precisam estar
    /// no campo. Vazio quando a carta não é ritual.</summary>
    public List<RitualRecipe> Rituals { get; set; } = [];

    /// <summary>Traduções da descrição por código de idioma (ex.: "pt",
    /// "es"). UI lê este map pelo locale atual; cai pro
    /// <see cref="Description"/> original quando ausente.</summary>
    public Dictionary<string, string> DescriptionsByLanguage { get; set; } = [];

    // Raw grayscale pixels (40×32) for OpenCV template matching
    public byte[]? ThumbnailPixels { get; set; }

    /// <summary>
    /// Data URL (image/bmp;base64) com o thumbnail colorido extraído do ROM.
    /// Permite renderizar a arte sem depender de servidor externo. Null
    /// antes de <c>LoadThumbnailsAsync</c> rodar.
    /// </summary>
    public string? ModImageDataUrl { get; set; }

    public string GetTitle() =>
        $"{Name} ({Attack} | {Defense})\t{GuardianStarName(GuardianStar1)} | {GuardianStarName(GuardianStar2)}";

    public override string ToString() =>
        $"{CardId}: {Name} ({CardType})\nA/D: {Attack} | {Defense}\n{Description}";

    private static string GuardianStarName(int index) =>
        index < GuardianStarNames.Length ? GuardianStarNames[index] : "?";

    /// <summary>
    /// Nomes dos guardian stars indexados por ID (0..13). Índices 0..10 são
    /// os originais do FM; 11..13 são extensões usadas por MODs:
    ///   • Fortuna (11): vence os 10 originais, perde pra Ceres, neutro com
    ///     Transpluto e contra outra Fortuna.
    ///   • Transpluto (12): neutro contra tudo.
    ///   • Ceres (13): perde para os 10 originais, vence Fortuna, neutro com
    ///     Transpluto.
    /// </summary>
    public static readonly string[] GuardianStarNames =
    [
        "None",
        "Mars", "Jupiter", "Saturn", "Uranus", "Pluto", "Neptune",
        "Mercury", "Sun", "Moon", "Venus",
        "Fortuna", "Transpluto", "Ceres",
    ];

}

/// <summary>
/// Receita de ritual: lista de monstros a serem oferecidos pra invocar
/// a carta-resultado. No FM clássico são 3 ingredientes; mods podem ter
/// número diferente, então a lista é flexível.
/// </summary>
public class RitualRecipe
{
    /// <summary>Card IDs 0-based dos monstros que precisam ser ofertados.</summary>
    public List<int> Ingredients { get; set; } = [];
    /// <summary>Card ID 0-based da carta resultante (= a própria, geralmente).</summary>
    public int Result { get; set; }
}
