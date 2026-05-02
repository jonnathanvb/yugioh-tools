using yugiho_tools.Domain.Entities;

namespace yugiho_tools.Domain.Interfaces;

public interface IModRepository
{
    Task<IReadOnlyList<Mod>> ListAsync();

    /// <summary>
    /// Registers a mod by copying both ROM files into MOD/&lt;slug&gt;/.
    /// </summary>
    Task<Mod> RegisterAsync(string name, string sourceGamePath, string sourceMrgPath);

    Task DeleteAsync(string slug);

    string GetGameFilePath(Mod mod);
    string GetMrgFilePath(Mod mod);
}
