using System.Buffers.Binary;

namespace yugiho_tools.Infrastructure.Parsing;

/// <summary>
/// Detecta empiricamente o offset da tabela de duelistas no
/// <c>WA_MRG.MRG</c>. Útil pra mods que relocaram a tabela — o offset
/// padrão (0xE9B000) só funciona pro FM-US original.
///
/// Assinatura procurada:
///   • 39 blocos de 0x1800 bytes consecutivos
///   • cada bloco começa com um pool de deck (722 uint16 LE) cuja soma
///     fica entre 1500 e 2600 (esperado: 2048)
///   • o segundo bloco (próximo duelista) também passa o critério
///
/// Sem precisar achar todos os 39 — basta encontrar 5 duelistas seguidos
/// começando no mesmo offset e a confiança já é altíssima (P(falso
/// positivo) ≈ 0 no espaço de busca real).
/// </summary>
public static class DuelistOffsetDetector
{
    private const int CardCount       = 722;
    private const int PoolBytes       = CardCount * 2;          // 1444
    private const int DuelistStride   = 0x1800;                 // 6144
    private const int DuelistCount    = 39;
    // Permite mods com denominador diferente (ex: 4096 em alguns mods)
    // ou com decks parcialmente vazios — TLM e Remaster cabem nessa janela.
    private const int MinDeckSum      = 1024;
    private const int MaxDeckSum      = 4500;
    private const int RequiredStreak  = 5;
    /// <summary>Espaço onde a tabela costuma viver (heurística do FM):
    /// no segundo terço do MRG. Estreitar reduz falsos positivos e custo.</summary>
    private const int ScanStart       = 0x600000;
    /// <summary>Granularidade do varredor. 16 = passo fino sem custo
    /// proibitivo (0x39000 bytes / 16 ≈ 14k iterações por busca, e
    /// abortamos cedo na maioria).</summary>
    private const int ScanStep        = 16;

    /// <summary>
    /// Devolve o offset detectado ou null se nada plausível foi achado.
    /// </summary>
    public static int? Detect(byte[] mrg)
    {
        if (mrg is null || mrg.Length < ScanStart + DuelistStride * DuelistCount)
            return null;

        int maxOffset = mrg.Length - DuelistStride * RequiredStreak - PoolBytes;

        for (int off = ScanStart; off < maxOffset; off += ScanStep)
        {
            if (LooksLikeDuelistTable(mrg, off)) return off;
        }
        return null;
    }

    /// <summary>
    /// Verifica se a partir de <paramref name="off"/> existem ao menos
    /// <see cref="RequiredStreak"/> blocos consecutivos cujo deck (primeiro
    /// pool) soma dentro do intervalo plausível.
    /// </summary>
    private static bool LooksLikeDuelistTable(byte[] mrg, int off)
    {
        int hits = 0;
        for (int i = 0; i < DuelistCount; i++)
        {
            int deckOff = off + i * DuelistStride;
            if (deckOff + PoolBytes > mrg.Length) return false;

            long sum = 0;
            var span = mrg.AsSpan(deckOff, PoolBytes);
            for (int k = 0; k < CardCount; k++)
            {
                sum += BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(k * 2, 2));
                // Early exit se a soma já passou do teto antes mesmo de
                // terminar — forte sinal de que não é um pool de pesos.
                if (sum > MaxDeckSum * 4) return false;
            }

            if (sum >= MinDeckSum && sum <= MaxDeckSum)
            {
                hits++;
                if (hits >= RequiredStreak) return true;
            }
            else
            {
                // Quebrou a sequência: este offset não é o início.
                return false;
            }
        }
        return hits >= RequiredStreak;
    }
}
