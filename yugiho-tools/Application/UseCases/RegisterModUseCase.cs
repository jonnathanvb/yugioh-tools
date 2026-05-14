using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;

namespace yugiho_tools.Application.UseCases;

public class ListModsUseCase(IModRepository repo)
{
    public Task<IReadOnlyList<Mod>> ExecuteAsync() => repo.ListAsync();
}

public class DeleteModUseCase(IModRepository repo)
{
    public Task ExecuteAsync(string slug) => repo.DeleteAsync(slug);
}
