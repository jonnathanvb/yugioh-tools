using yugiho_tools.Application.DTOs;
using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;

namespace yugiho_tools.Application.UseCases;

public class GetFusionsFromHandUseCase(IFusionEngine engine)
{
    public IReadOnlyList<FusionSequence> Execute(
        IReadOnlyList<int> hand,
        IReadOnlyList<Card> cards) =>
        engine.GetFusionsFromHand(hand, cards);
}
