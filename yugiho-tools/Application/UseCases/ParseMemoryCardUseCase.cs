using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;

namespace yugiho_tools.Application.UseCases;

public class ParseMemoryCardUseCase(IMemoryCardParser parser)
{
    public Task<MemoryCardParseResult> ExecuteAsync(string filePath) =>
        parser.ParseAsync(filePath);
}
