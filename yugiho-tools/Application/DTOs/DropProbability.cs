namespace yugiho_tools.Application.DTOs;

/// <summary>
/// One drop chance entry for a card from a specific duelist + pool.
/// Probability is <c>Weight / 2048</c>.
/// </summary>
public sealed record DropProbability(
    int DuelistId,
    string DuelistName,
    DropPoolKind Pool,
    int Weight)
{
    public double Percent => Weight / 2048.0 * 100.0;
}

public enum DropPoolKind
{
    SaPow,
    SaTec,
    BcdPow,
}
