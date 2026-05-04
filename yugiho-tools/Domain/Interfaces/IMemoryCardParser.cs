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
}
