using yugiho_tools.Domain.Entities;

namespace yugiho_tools.Domain.Interfaces;

public interface IMemoryCardParser
{
    Task<MemoryCardParseResult> ParseAsync(string filePath);
    MemoryCardParseResult Parse(byte[] memoryCardBytes);
}
