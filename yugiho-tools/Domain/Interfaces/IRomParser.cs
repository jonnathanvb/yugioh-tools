using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.ValueObjects;

namespace yugiho_tools.Domain.Interfaces;

public interface IRomParser
{
    Task<RomData> ParseAsync(
        string gameFilePath,
        string mrgFilePath,
        RomOffsetProfile? profile = null,
        IProgress<int>? progress = null);

    Task LoadThumbnailsAsync(
        IReadOnlyList<Card> cards,
        string mrgFilePath,
        RomOffsetProfile? profile = null,
        IProgress<int>? progress = null);
}

public sealed record RomData(
    IReadOnlyList<Card> Cards,
    IReadOnlyList<Duelist> Duelists);
