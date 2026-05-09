namespace yugiho_tools.Application.Helpers;

/// <summary>
/// Cache de imagens extraídas (nome, atributo, ícone de estrela) que
/// enriquecem a moldura do modo MOD. Populado pelo
/// <c>ExtractedDataLoader</c> ao carregar o MOD do disco.
///
/// Como o nosso encoder BMP é 24bpp sem alpha, o fundo preto fica
/// visível. A UI compõe usando <c>mix-blend-mode: lighten</c> sobre o
/// frame dourado — pixels pretos somem, nome dourado fica visível.
/// </summary>
public static class ExtractedAssets
{
    /// <summary>cardId (1-based) → data URL da imagem do nome 96×14.</summary>
    private static readonly Dictionary<int, string> Names = new(722);
    /// <summary>attrId (0..8) → data URL do ícone 16×16.</summary>
    private static readonly Dictionary<int, string> Attributes = new(9);
    /// <summary>duelistId (0..39) → data URL do portrait 48×48.</summary>
    private static readonly Dictionary<int, string> Duelists = new(40);
    /// <summary>typeId (0..23) → data URL do ícone de tipo 16×16.
    /// Usado tanto na tabela de tipos quanto inline na descrição via
    /// marcador <c>&lt;_N_&gt;</c>.</summary>
    private static readonly Dictionary<int, string> Types = new(24);
    private static string? StarUrl;

    public static bool HasNames      => Names.Count > 0;
    public static bool HasAttributes => Attributes.Count > 0;
    public static bool HasDuelists   => Duelists.Count > 0;
    public static bool HasTypes      => Types.Count > 0;
    public static bool HasStar       => !string.IsNullOrEmpty(StarUrl);

    public static string? GetName(int cardId)
        => Names.TryGetValue(cardId, out var url) ? url : null;

    public static string? GetAttribute(int attrId)
        => Attributes.TryGetValue(attrId, out var url) ? url : null;

    public static string? GetDuelist(int duelistId)
        => Duelists.TryGetValue(duelistId, out var url) ? url : null;

    public static string? GetType(int typeId)
        => Types.TryGetValue(typeId, out var url) ? url : null;

    public static string? GetStar() => StarUrl;

    public static void Reset()
    {
        Names.Clear();
        Attributes.Clear();
        Duelists.Clear();
        Types.Clear();
        StarUrl = null;
    }

    public static void SetName(int cardId, string url)      => Names[cardId] = url;
    public static void SetAttribute(int attrId, string url) => Attributes[attrId] = url;
    public static void SetDuelist(int duelistId, string url) => Duelists[duelistId] = url;
    public static void SetType(int typeId, string url)       => Types[typeId] = url;
    public static void SetStar(string url)                  => StarUrl = url;
}
