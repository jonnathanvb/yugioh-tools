using yugiho_tools.Application.DTOs;
using yugiho_tools.Application.Helpers;
using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;
using yugiho_tools.Domain.ValueObjects;

namespace yugiho_tools.Application.UseCases;

public class LoadRomDataUseCase(IRomParser parser)
{
    public async Task<LoadedRomData> ExecuteAsync(
        string gameFilePath,
        string mrgFilePath,
        bool loadThumbnails,
        RomOffsetProfile? profile = null,
        IProgress<int>? progress = null)
    {
        var data = await parser.ParseAsync(gameFilePath, mrgFilePath, profile, progress);

        if (loadThumbnails)
        {
            await parser.LoadThumbnailsAsync(data.Cards, mrgFilePath, profile, progress);
            // Atualiza o registry compartilhado para que toda chamada a
            // CardImage.Url possa servir a arte do ROM quando o usuário
            // escolher a fonte "MOD" nas configurações.
            CardImage.LoadFromCards(data.Cards);
        }

        return new LoadedRomData(data.Cards, data.Duelists);
    }
}

public sealed record LoadedRomData(
    IReadOnlyList<Card> Cards,
    IReadOnlyList<Duelist> Duelists)
{
    /// <summary>
    /// Returns every duelist that drops <paramref name="cardIndex"/> across the
    /// three rank pools, with their probability weights. Empty list = no drops.
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
