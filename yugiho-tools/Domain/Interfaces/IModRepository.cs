using yugiho_tools.Domain.Entities;

namespace yugiho_tools.Domain.Interfaces;

/// <summary>
/// Repositório dos MODs instalados (extraídos do ZIP do catálogo). O
/// app só consome dados via JSON — não há mais leitura direta de SLUS/MRG
/// nesta interface; isso ficou no <c>yugiho-download-json</c>.
/// </summary>
public interface IModRepository
{
    Task<IReadOnlyList<Mod>> ListAsync();

    /// <summary>
    /// Registra um mod já extraído via importação de ZIP. A pasta
    /// <c>MODs/{slug}/</c> com data.json + assets deve existir antes
    /// da chamada — o repositório só atualiza o índice mods.json.
    /// </summary>
    Task<Mod> RegisterImportedAsync(string name, string folderName);

    /// <summary>Atualiza um MOD existente (matched por <c>Slug</c>).
    /// Reescreve o index inteiro com a nova entrada.</summary>
    Task UpdateAsync(Mod mod);

    Task DeleteAsync(string slug);

    /// <summary>Pasta raiz do MOD em disco (<c>MOD/{slug}/</c>) — usada
    /// pra ler data.json e os subdirs de assets (cards/, frames/, etc.).</summary>
    string GetModFolderPath(Mod mod);
}
