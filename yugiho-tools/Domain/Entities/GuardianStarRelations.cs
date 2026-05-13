namespace yugiho_tools.Domain.Entities;

/// <summary>
/// Relações de vitória/derrota entre Guardian Stars do FM e dos MODs.
///
/// Dois ciclos originais:
///   Sun → Moon → Venus → Mercury → Sun
///   Mars → Jupiter → Saturn → Uranus → Pluto → Neptune → Mars
///
/// Extensões MOD:
///   Fortuna (11)    vence todos os 10 originais; perde para Ceres;
///                   neutro com Transpluto e Fortuna vs Fortuna.
///   Transpluto (12) neutro contra tudo (inclusive Transpluto).
///   Ceres (13)      perde para todos os 10 originais; vence Fortuna;
///                   neutro com Transpluto.
/// </summary>
public static class GuardianStarRelations
{
    public const int None       = 0;
    public const int Mars       = 1;
    public const int Jupiter    = 2;
    public const int Saturn     = 3;
    public const int Uranus     = 4;
    public const int Pluto      = 5;
    public const int Neptune    = 6;
    public const int Mercury    = 7;
    public const int Sun        = 8;
    public const int Moon       = 9;
    public const int Venus      = 10;
    public const int Fortuna    = 11;
    public const int Transpluto = 12;
    public const int Ceres      = 13;

    /// <summary>IDs dos 10 originais (Mars..Venus). Usado pra Fortuna/Ceres
    /// que vencem/perdem contra "todos os originais".</summary>
    public static readonly int[] Originals =
        [Mars, Jupiter, Saturn, Uranus, Pluto, Neptune, Mercury, Sun, Moon, Venus];

    /// <summary>Próximo elo no ciclo solar (vence quem vem depois).</summary>
    private static readonly Dictionary<int, int> SolarNext = new()
    {
        [Sun]     = Moon,
        [Moon]    = Venus,
        [Venus]   = Mercury,
        [Mercury] = Sun,
    };

    /// <summary>Próximo elo no ciclo planetário.</summary>
    private static readonly Dictionary<int, int> PlanetNext = new()
    {
        [Mars]    = Jupiter,
        [Jupiter] = Saturn,
        [Saturn]  = Uranus,
        [Uranus]  = Pluto,
        [Pluto]   = Neptune,
        [Neptune] = Mars,
    };

    /// <summary>
    /// Retorna as Guardian Stars que <paramref name="starId"/> vence.
    /// Lista vazia = não vence nenhuma (neutro/None/Transpluto).
    /// Múltiplas entradas = vence várias (Fortuna vence todos os originais).
    /// </summary>
    public static IReadOnlyList<int> Beats(int starId)
    {
        if (SolarNext.TryGetValue(starId,  out var s)) return [s];
        if (PlanetNext.TryGetValue(starId, out var p)) return [p];
        return starId switch
        {
            Fortuna => Originals,
            Ceres   => [Fortuna],
            _       => [],         // None, Transpluto
        };
    }

    /// <summary>
    /// Retorna as Guardian Stars que vencem <paramref name="starId"/>.
    /// Implementado como inverso de <see cref="Beats"/>.
    /// </summary>
    public static IReadOnlyList<int> BeatenBy(int starId)
    {
        // Originais: predecessor no ciclo + Fortuna (que vence todos os originais).
        foreach (var (k, v) in SolarNext)
            if (v == starId) return [k, Fortuna];
        foreach (var (k, v) in PlanetNext)
            if (v == starId) return [k, Fortuna];
        return starId switch
        {
            Fortuna => [Ceres],
            Ceres   => Originals,   // perde para todos os originais
            _       => [],          // None, Transpluto
        };
    }
}
