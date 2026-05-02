using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;

namespace yugiho_tools.Application.UseCases;

public class LoadRomDataUseCase(IRomParser parser)
{
    public async Task<IReadOnlyList<Card>> ExecuteAsync(
        string gameFilePath,
        string mrgFilePath,
        bool loadThumbnails,
        IProgress<int>? progress = null)
    {
        var cards = await parser.ParseAsync(gameFilePath, mrgFilePath, progress);

        if (loadThumbnails)
            await parser.LoadThumbnailsAsync(cards, mrgFilePath, progress);

        return cards;
    }
}
