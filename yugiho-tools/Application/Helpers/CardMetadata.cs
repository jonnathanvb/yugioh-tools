namespace yugiho_tools.Application.Helpers;

public static class CardMetadata
{
    public static readonly string[] AttributeNames =
    [
        "Light", "Dark", "Earth", "Water", "Fire", "Wind", "Magic", "Trap"
    ];

    public static readonly string[] TypeNames =
    [
        "Dragon", "Spellcaster", "Zombie", "Warrior", "Beast-Warrior",
        "Beast", "Winged Beast", "Fiend", "Fairy", "Insect",
        "Dinosaur", "Psychic", "Fish", "Divine-Beast", "Machine",
        "Thunder", "Aqua", "Pyro"
    ];

    public static string AttributeName(int id) =>
        id >= 0 && id < AttributeNames.Length ? AttributeNames[id] : "?";

    public static string TypeName(int id) =>
        id >= 0 && id < TypeNames.Length ? TypeNames[id] : "?";
}
