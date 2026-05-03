using System.Text.Json;

namespace yugiho_tools.Application.Helpers;

public record CardCategoryEntry(int Id, string Name);

/// <summary>
/// Catálogo de tipos e atributos de carta. Os valores são carregados dos
/// arquivos JSON empacotados (<c>card-types.json</c> e
/// <c>card-attributes.json</c>) na primeira chamada de <see cref="LoadAsync"/>.
/// Antes do load, fallback hardcoded é usado.
/// </summary>
public static class CardMetadata
{
    private static IReadOnlyList<CardCategoryEntry> _types      = DefaultTypes;
    private static IReadOnlyList<CardCategoryEntry> _attributes = DefaultAttributes;
    private static volatile bool _loaded;
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public static IReadOnlyList<CardCategoryEntry> Types      => _types;
    public static IReadOnlyList<CardCategoryEntry> Attributes => _attributes;

    public static string TypeName(int id) =>
        _types.FirstOrDefault(t => t.Id == id)?.Name ?? "?";

    public static string AttributeName(int id) =>
        _attributes.FirstOrDefault(a => a.Id == id)?.Name ?? "?";

    public static async Task LoadAsync()
    {
        if (_loaded) return;
        await _gate.WaitAsync();
        try
        {
            if (_loaded) return;

            var types = await LoadJsonAsync("card-types.json");
            if (types is { Count: > 0 }) _types = types;

            var attrs = await LoadJsonAsync("card-attributes.json");
            if (attrs is { Count: > 0 }) _attributes = attrs;

            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<IReadOnlyList<CardCategoryEntry>?> LoadJsonAsync(string asset)
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(asset);
            return await JsonSerializer.DeserializeAsync<List<CardCategoryEntry>>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    // ── Fallback (offline): mantém o app funcional se o asset falhar ────
    private static readonly CardCategoryEntry[] DefaultTypes =
    [
        new(0,  "Dragon"),        new(1,  "Spellcaster"),
        new(2,  "Zombie"),        new(3,  "Warrior"),
        new(4,  "Beast-Warrior"), new(5,  "Beast"),
        new(6,  "Winged Beast"),  new(7,  "Fiend"),
        new(8,  "Fairy"),         new(9,  "Insect"),
        new(10, "Dinosaur"),      new(11, "Psychic"),
        new(12, "Fish"),          new(13, "Divine-Beast"),
        new(14, "Machine"),       new(15, "Thunder"),
        new(16, "Aqua"),          new(17, "Pyro"),
    ];

    private static readonly CardCategoryEntry[] DefaultAttributes =
    [
        new(0, "Light"), new(1, "Dark"),
        new(2, "Earth"), new(3, "Water"),
        new(4, "Fire"),  new(5, "Wind"),
        new(6, "Magic"), new(7, "Trap"),
    ];
}
