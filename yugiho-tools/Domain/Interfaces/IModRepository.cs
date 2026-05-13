using yugiho_tools.Domain.Entities;

namespace yugiho_tools.Domain.Interfaces;

public interface IModRepository
{
    Task<IReadOnlyList<Mod>> ListAsync();

    /// <summary>
    /// Registers a mod by copying both ROM files into MOD/&lt;slug&gt;/.
    /// </summary>
    Task<Mod> RegisterAsync(
        string name,
        string sourceGamePath,
        string sourceMrgPath,
        string imageUrlTemplate);

    /// <summary>Atualiza um MOD existente (matched por <c>Slug</c>).
    /// Reescreve o index inteiro com a nova entrada.</summary>
    Task UpdateAsync(Mod mod);

    Task DeleteAsync(string slug);

    string GetGameFilePath(Mod mod);
    string GetMrgFilePath(Mod mod);

    /// <summary>Pasta raiz do MOD em disco (<c>MOD/{slug}/</c>) — usada
    /// pra guardar artefatos extraídos (data.json, imagens decodificadas).</summary>
    string GetModFolderPath(Mod mod);
}
