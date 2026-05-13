using yugiho_tools.Domain.Entities;

namespace yugiho_tools.Domain.Interfaces;

public interface IMemoryCardParser
{
    Task<MemoryCardParseResult> ParseAsync(string filePath);
    MemoryCardParseResult Parse(byte[] memoryCardBytes);

    /// <summary>
    /// Localiza o trunk (coleção de 722 quantidades) dentro do save FM.
    /// Retorna null se não encontrar.
    /// </summary>
    MemoryCardTrunk? FindTrunk(byte[] memoryCardBytes, MemoryCardSave save);

    /// <summary>
    /// Reescreve as 722 entradas do trunk no arquivo .mcr no offset
    /// previamente identificado por <see cref="FindTrunk"/>.
    /// </summary>
    Task SaveTrunkAsync(
        string filePath,
        MemoryCardTrunk trunk,
        IReadOnlyDictionary<int, int> counts);

    /// <summary>Lê o contador de saves (games-played) do FM save.</summary>
    MemoryCardSaveCounter? ReadSaveCounter(byte[] memoryCardBytes, MemoryCardSave save);

    /// <summary>Reescreve o contador no arquivo, atualizando as 2 posições
    /// (main + backup) e recalculando os CRCs.</summary>
    Task WriteSaveCounterAsync(
        string filePath,
        MemoryCardSave save,
        int newCounter);
}
