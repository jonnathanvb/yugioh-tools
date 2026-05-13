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

/// <summary>
/// Contador de saves do FM. O jogo grava em duas posições dentro do save
/// block (0x604 e 0x60A relativos ao block_start) e espelha tudo na backup
/// half (+0x680). Quando o usuário carrega um savestate antigo, o contador
/// em RAM fica menor que o do memory card — e o jogo mostra "Unable to
/// locate load data!". A correção é gravar um valor menor (ou igual) no
/// memory card pra "destravar" o load.
/// </summary>
public record MemoryCardSaveCounter(
    MemoryCardSave Save,
    int Counter,           // valor atual lido em 0x604
    int CounterCheck,      // valor em 0x60A (geralmente Counter + 1)
    int OffsetA,           // posição absoluta do byte 0x604
    int OffsetB);          // posição absoluta do byte 0x60A
