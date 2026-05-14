using System.Text.Json;
using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;

namespace yugiho_tools.Infrastructure.Storage;

public class FileModRepository : IModRepository
{
    /// <summary>
    /// Raiz dos mods em disco. AppDataDirectory é writable em todas as
    /// plataformas MAUI (Windows AppData, macOS ~/Library/Application Support).
    /// </summary>
    private static readonly string ModRoot =
        Path.Combine(FileSystem.AppDataDirectory, "MODs");

    private static readonly string IndexPath =
        Path.Combine(ModRoot, "mods.json");

    private static readonly JsonSerializerOptions JsonOpts =
        new() { WriteIndented = true };

    public async Task<IReadOnlyList<Mod>> ListAsync()
    {
        if (!File.Exists(IndexPath)) return [];

        try
        {
            await using var fs = File.OpenRead(IndexPath);
            var list = await JsonSerializer.DeserializeAsync<List<Mod>>(fs, JsonOpts);
            return list ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Registra um mod já extraído (pasta com data.json + assets), sem
    /// cópia de SLUS/MRG. Usado pelo importador de catálogo — o ZIP é
    /// descompactado direto em <c>MODs/{slug}/</c>.
    /// </summary>
    public async Task<Mod> RegisterImportedAsync(string name, string folderName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Nome do mod é obrigatório.", nameof(name));
        if (string.IsNullOrWhiteSpace(folderName))
            throw new ArgumentException("Pasta inválida.", nameof(folderName));

        var slug = Slugify(folderName);
        if (string.IsNullOrEmpty(slug))
            throw new ArgumentException("Nome inválido para slug.", nameof(folderName));

        var existing = (await ListAsync()).ToList();
        // Substitui se já existir — o caller acabou de reextrair o ZIP.
        existing.RemoveAll(m => string.Equals(m.Slug, slug, StringComparison.OrdinalIgnoreCase));

        var mod = new Mod
        {
            Name             = name.Trim(),
            Slug             = slug,
            GameFileName     = "",
            MrgFileName      = "",
            ImageUrlTemplate = "",
            CreatedAt        = DateTime.Now,
            ImageSource      = Domain.Entities.ImageSource.Mod,
        };

        existing.Add(mod);
        await SaveIndexAsync(existing);
        return mod;
    }

    public async Task<Mod> RegisterAsync(
        string name,
        string sourceGamePath,
        string sourceMrgPath,
        string imageUrlTemplate)
    {
        // Caminho legado mantido como compat: yugioh-tools não cria
        // mais mods via SLUS/MRG (isso migrou pro yugiho-download-json).
        // Chamadas remanescentes lançam para evitar regressão silenciosa.
        throw new NotSupportedException(
            "Cadastro direto via SLUS/MRG não é mais suportado neste app. " +
            "Use o importador de catálogo em /mods.");
    }

    public async Task UpdateAsync(Mod mod)
    {
        var list = (await ListAsync()).ToList();
        var idx  = list.FindIndex(m => m.Slug == mod.Slug);
        if (idx < 0) throw new InvalidOperationException(
            $"MOD '{mod.Slug}' não encontrado pra atualizar.");
        list[idx] = mod;
        await SaveIndexAsync(list);
    }

    public async Task DeleteAsync(string slug)
    {
        var list = (await ListAsync()).ToList();
        var idx  = list.FindIndex(m => m.Slug == slug);
        if (idx < 0) return;

        var folder = Path.Combine(ModRoot, slug);
        if (Directory.Exists(folder))
        {
            try { Directory.Delete(folder, recursive: true); }
            catch { /* best effort */ }
        }

        list.RemoveAt(idx);
        await SaveIndexAsync(list);
    }

    public string GetGameFilePath(Mod mod) =>
        Path.Combine(ModRoot, mod.Slug, mod.GameFileName);

    public string GetMrgFilePath(Mod mod) =>
        Path.Combine(ModRoot, mod.Slug, mod.MrgFileName);

    public string GetModFolderPath(Mod mod) =>
        Path.Combine(ModRoot, mod.Slug);

    /// <summary>Raiz onde os mods importados são descompactados.</summary>
    public static string GetModsRoot() => ModRoot;

    private static async Task SaveIndexAsync(IReadOnlyList<Mod> mods)
    {
        Directory.CreateDirectory(ModRoot);
        await using var fs = File.Create(IndexPath);
        await JsonSerializer.SerializeAsync(fs, mods, JsonOpts);
    }

    public static string Slugify(string name)
    {
        var s = name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        s = s.Replace(' ', '_');
        while (s.Contains("__")) s = s.Replace("__", "_");
        return s.Trim('_', '.');
    }
}
