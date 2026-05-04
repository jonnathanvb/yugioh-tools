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

/// <summary>
/// Representa o "trunk" (coleção completa) extraído de um save de FM —
/// 722 entradas onde cada byte = quantidade da carta correspondente.
/// </summary>
public record MemoryCardTrunk(
    MemoryCardSave Save,
    int    OffsetInFile,    // posição absoluta dentro do .mcr
    int    BytesPerEntry,   // 1 (byte-per-card) ou 2 (ushort LE)
    byte[] Counts);         // sempre length == 722 (índice = CardId - 1)
