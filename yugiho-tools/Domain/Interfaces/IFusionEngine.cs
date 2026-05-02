using yugiho_tools.Application.DTOs;
using yugiho_tools.Domain.Entities;

namespace yugiho_tools.Domain.Interfaces;

public interface IFusionEngine
{
    /// <summary>
    /// Returns all possible fusion sequences from a given hand.
    /// Each sequence shows every step (card1 + card2 = intermediate/final).
    /// Results are sorted by final card attack minus number of fusion steps.
    /// </summary>
    IReadOnlyList<FusionSequence> GetFusionsFromHand(
        IReadOnlyList<int> hand,
        IReadOnlyList<Card> cards);
}
