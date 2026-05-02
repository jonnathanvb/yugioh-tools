using yugiho_tools.Domain.Entities;

namespace yugiho_tools.Domain.Interfaces;

public interface ICardDetector
{
    /// <summary>
    /// Detects cards in a screenshot (raw BGR bytes, 320x240 or any size).
    /// Returns indices into the cards list.
    /// </summary>
    IReadOnlyList<int> DetectCards(byte[] imageBytes, IReadOnlyList<Card> cards);
}
