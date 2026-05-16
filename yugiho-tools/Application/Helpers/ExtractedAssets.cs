using System.Collections.Concurrent;

namespace yugiho_tools.Application.Helpers;

/// <summary>
/// Cache de imagens extraídas (nome, atributo, ícone de estrela) que
/// enriquecem a moldura do modo MOD. Populado pelo
/// <c>ExtractedDataLoader</c> ao carregar o MOD do disco.
///
/// Imagens são PNG com canal alpha (índice 0 da CLUT vira pixel
/// transparente), então a UI não precisa de truques de blend-mode —
/// basta posicionar a img sobre o frame.
///
/// <para>Thread-safety: o load roda em <c>Task.Run</c> (background),
/// enquanto a UI lê continuamente via <c>Get*</c> durante render.
/// Usamos <see cref="ConcurrentDictionary{TKey,TValue}"/> pra que reads
/// não disparem <c>InvalidOperationException</c> quando o background
/// está limpando/preenchendo. <see cref="StarUrl"/> é trocado por
/// referência atômica via <c>Volatile.Write</c>.</para>
/// </summary>
public static class ExtractedAssets
{
    /// <summary>cardId (1-based) → data URL da imagem do nome 96×14.</summary>
    private static readonly ConcurrentDictionary<int, string> Names      = new();
    /// <summary>attrId (0..8) → data URL do ícone 16×16.</summary>
    private static readonly ConcurrentDictionary<int, string> Attributes = new();
    /// <summary>duelistId (0..39) → data URL do portrait 48×48.</summary>
    private static readonly ConcurrentDictionary<int, string> Duelists   = new();
    /// <summary>typeId (0..23) → data URL do ícone de tipo 16×16.
    /// Usado tanto na tabela de tipos quanto inline na descrição via
    /// marcador <c>&lt;_N_&gt;</c>.</summary>
    private static readonly ConcurrentDictionary<int, string> Types      = new();
    /// <summary>guardianId (1..13) → data URL do ícone 16×16. Índice
    /// alinhado com Card.GuardianStarNames (0 = None, sem ícone).</summary>
    private static readonly ConcurrentDictionary<int, string> Guardians  = new();
    private static string? _starUrl;

    public static bool HasNames      => !Names.IsEmpty;
    public static bool HasAttributes => !Attributes.IsEmpty;
    public static bool HasDuelists   => !Duelists.IsEmpty;
    public static bool HasTypes      => !Types.IsEmpty;
    public static bool HasGuardians  => !Guardians.IsEmpty;
    public static bool HasStar       => !string.IsNullOrEmpty(_starUrl);

    public static string? GetName(int cardId)
        => Names.TryGetValue(cardId, out var url) ? url : null;

    public static string? GetAttribute(int attrId)
        => Attributes.TryGetValue(attrId, out var url) ? url : null;

    public static string? GetDuelist(int duelistId)
        => Duelists.TryGetValue(duelistId, out var url) ? url : null;

    public static string? GetType(int typeId)
        => Types.TryGetValue(typeId, out var url) ? url : null;

    public static string? GetGuardian(int guardianId)
        => Guardians.TryGetValue(guardianId, out var url) ? url : null;

    public static string? GetStar()
        => System.Threading.Volatile.Read(ref _starUrl);

    public static void Reset()
    {
        Names.Clear();
        Attributes.Clear();
        Duelists.Clear();
        Types.Clear();
        Guardians.Clear();
        System.Threading.Volatile.Write(ref _starUrl, null);
    }

    public static void SetName(int cardId, string url)         => Names[cardId]       = url;
    public static void SetAttribute(int attrId, string url)    => Attributes[attrId]  = url;
    public static void SetDuelist(int duelistId, string url)   => Duelists[duelistId] = url;
    public static void SetType(int typeId, string url)         => Types[typeId]       = url;
    public static void SetGuardian(int guardianId, string url) => Guardians[guardianId] = url;
    public static void SetStar(string url)
        => System.Threading.Volatile.Write(ref _starUrl, url);

    // Clears parciais — usados pelo SharedImagesService quando o usuário
    // troca a variante (SD/HD/SD MOD/HD MOD) sem precisar resetar o resto
    // dos assets (names, duelists, guardians vêm do per-MOD).
    public static void ClearAttributes() => Attributes.Clear();
    public static void ClearTypes()      => Types.Clear();
    public static void ClearStar()
        => System.Threading.Volatile.Write(ref _starUrl, null);
}
