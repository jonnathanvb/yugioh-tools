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

    public int FusionCount { get; set; }
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

    // Raw grayscale pixels (40×32) for OpenCV template matching
    public byte[]? ThumbnailPixels { get; set; }

    public string GetTitle() =>
        $"{Name} ({Attack} | {Defense})\t{GuardianStarName(GuardianStar1)} | {GuardianStarName(GuardianStar2)}";

    public override string ToString() =>
        $"{CardId}: {Name} ({CardType})\nA/D: {Attack} | {Defense}\n{Description}";

    private static string GuardianStarName(int index) =>
        index < GuardianStarNames.Length ? GuardianStarNames[index] : "?";

    public static readonly string[] GuardianStarNames =
        ["None", "Mars", "Jupiter", "Saturn", "Uranus", "Pluto", "Neptune", "Mercury", "Sun", "Moon", "Venus"];
    
}
