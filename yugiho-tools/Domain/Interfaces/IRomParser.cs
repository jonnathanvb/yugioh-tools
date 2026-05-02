using yugiho_tools.Domain.Entities;

namespace yugiho_tools.Domain.Interfaces;

public interface IRomParser
{
    Task<IReadOnlyList<Card>> ParseAsync(
        string gameFilePath,
        string mrgFilePath,
        IProgress<int>? progress = null);

    Task LoadThumbnailsAsync(
        IReadOnlyList<Card> cards,
        string mrgFilePath,
        IProgress<int>? progress = null);
}
