using yugiho_tools.Application.DTOs;

namespace yugiho_tools.Domain.Entities;

/// <summary>
/// Snapshot dos dados de um mod já carregado em memória — cartas,
/// duelistas e posições do frame. Construído pelo
/// <c>ExtractedDataLoader</c> a partir do <c>data.json</c> em
/// <c>MODs/{slug}/</c>.
///
/// O nome legado <c>LoadedRomData</c> foi mantido pra não quebrar
/// todas as referências em razor pages durante a migração; semanticamente
/// agora representa "mod data" e não "rom data".
/// </summary>
public sealed record LoadedRomData(
    IReadOnlyList<Card> Cards,
    IReadOnlyList<Duelist> Duelists,
    FramePositions? Positions = null)
{
    /// <summary>
    /// Retorna todos os duelistas que dropam <paramref name="cardIndex"/>
    /// nas três tabelas de pool (sap/sat/bcd), com peso. Lista vazia = sem drops.
    /// </summary>
    public IReadOnlyList<DropProbability> GetDrops(int cardIndex)
    {
        if ((uint)cardIndex >= 722) return [];
        var result = new List<DropProbability>();
        foreach (var d in Duelists)
        {
            int sap = d.SaPow [cardIndex];
            int sat = d.SaTec [cardIndex];
            int bcd = d.BcdPow[cardIndex];
            if (sap > 0) result.Add(new DropProbability(d.Id, d.Name, DropPoolKind.SaPow,  sap));
            if (sat > 0) result.Add(new DropProbability(d.Id, d.Name, DropPoolKind.SaTec,  sat));
            if (bcd > 0) result.Add(new DropProbability(d.Id, d.Name, DropPoolKind.BcdPow, bcd));
        }
        return result;
    }
}
