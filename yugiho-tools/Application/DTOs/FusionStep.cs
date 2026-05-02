using yugiho_tools.Domain.Entities;

namespace yugiho_tools.Application.DTOs;

/// <summary>One fusion step: Card1 + Card2 = Result</summary>
public record FusionStep(string Card1, string Card2, string Result);

/// <summary>
/// A complete fusion sequence.
/// Single fusion: 1 step (A + B = Final).
/// Multi-fusion: N steps (A + B = X, X + C = Final).
/// </summary>
public record FusionSequence(IReadOnlyList<FusionStep> Steps, Card FinalCard);
