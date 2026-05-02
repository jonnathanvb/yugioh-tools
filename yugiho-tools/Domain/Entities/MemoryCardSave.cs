namespace yugiho_tools.Domain.Entities;

public record MemoryCardSave(
    int    SlotIndex,
    string GameCode,
    string SaveId,
    int    StartBlock,
    int    BlockCount,
    bool   IsForbiddenMemories,
    IReadOnlyList<MemoryCardCardEntry> Cards);

public record MemoryCardCardEntry(int CardId, int Count);

public record MemoryCardParseResult(
    IReadOnlyList<MemoryCardSave> Saves);
