using yugiho_tools.Domain.Entities;

namespace yugiho_tools.Domain.Interfaces;

public interface IModRepository
{
    Task<IReadOnlyList<Mod>> ListAsync();

    /// <summary>
    /// Registra um mod já extraído via importação de ZIP. A pasta
    /// <c>MODs/{slug}/</c> com data.json + assets deve existir antes
    /// da chamada — o repositório só atualiza o índice mods.json.
    /// </summary>
    Task<Mod> RegisterImportedAsync(string name, string folderName);

    /// <summary>[Obsoleto] Cadastro direto via SLUS/MRG saiu do app —
    /// agora é feito no yugiho-download-json. Mantido na interface só
    /// pra não quebrar callers durante a migração.</summary>
    [Obsolete("Use RegisterImportedAsync; extração movida pro yugiho-download.")]
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
