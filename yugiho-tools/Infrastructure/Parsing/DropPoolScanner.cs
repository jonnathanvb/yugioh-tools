using System.Buffers.Binary;

namespace yugiho_tools.Infrastructure.Parsing;

/// <summary>
/// Diagnóstico: dado o base address de um duelista, varre os 0x1800 bytes
/// do slot procurando por TODAS as regiões de 1444 bytes (722 × uint16
/// little-endian) cuja soma seja plausível pra um pool de pesos.
///
/// Útil quando o detector achou os decks (offset 0x000 OK) mas os drops
/// padrão (0x5B4 / 0xB68 / 0x111C) estão zerados — sinal de que o mod
/// usa layout interno diferente.
/// </summary>
public static class DropPoolScanner
{
    private const int CardCount     = 722;
    private const int PoolBytes     = CardCount * 2;          // 1444
    private const int DuelistStride = 0x1800;
    /// <summary>Limites pra "soma plausível": cobrem denominador 2048
    /// e 4096, com folga pra pools parcialmente preenchidos.</summary>
    private const int MinSum        = 512;
    private const int MaxSum        = 8192;

    public readonly record struct Candidate(int OffsetInBlock, int Sum, int NonZeroCount);

    /// <summary>
    /// Lista todos os offsets internos (dentro de 0..0x1800-1444) onde
    /// existe um pool plausível. Granularidade de 4 bytes — fina o
    /// suficiente pra pegar qualquer alinhamento de struct conhecido.
    /// </summary>
    public static List<Candidate> Scan(byte[] mrg, int duelistBase)
    {
        var result = new List<Candidate>();
        if (mrg is null || duelistBase < 0
            || duelistBase + DuelistStride > mrg.Length)
        {
            return result;
        }

        for (int off = 0; off + PoolBytes <= DuelistStride; off += 4)
        {
            int absolute = duelistBase + off;
            long sum = 0;
            int nz = 0;
            var span = mrg.AsSpan(absolute, PoolBytes);
            for (int k = 0; k < CardCount; k++)
            {
                ushort v = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(k * 2, 2));
                if (v > 0) nz++;
                sum += v;
                if (sum > MaxSum * 4) { sum = -1; break; }
            }
            if (sum >= MinSum && sum <= MaxSum)
                result.Add(new Candidate(off, (int)sum, nz));
        }
        return result;
    }

    /// <summary>
    /// Roda o <see cref="Scan"/> nos primeiros <paramref name="sample"/>
    /// duelistas e devolve os offsets que aparecem em TODOS — esses
    /// são fortes candidatos a "pools sistemáticos" da estrutura.
    /// </summary>
    public static List<int> CommonOffsets(byte[] mrg, int duelistBase,
                                          int duelistStride = DuelistStride,
                                          int sample = 5)
    {
        if (sample < 1) sample = 1;
        var perDuelist = new List<HashSet<int>>(sample);
        for (int i = 0; i < sample; i++)
        {
            var cands = Scan(mrg, duelistBase + duelistStride * i);
            perDuelist.Add(cands.Select(c => c.OffsetInBlock).ToHashSet());
        }
        if (perDuelist.Count == 0) return [];

        var common = new HashSet<int>(perDuelist[0]);
        for (int i = 1; i < perDuelist.Count; i++)
            common.IntersectWith(perDuelist[i]);

        return common.OrderBy(x => x).ToList();
    }
}
