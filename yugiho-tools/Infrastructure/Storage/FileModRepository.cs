using System.Text.Json;
using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;

namespace yugiho_tools.Infrastructure.Storage;

public class FileModRepository : IModRepository
{
    private static readonly string ModRoot =
        Path.Combine(AppContext.BaseDirectory, "MOD");

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

    public async Task<Mod> RegisterAsync(
        string name,
        string sourceGamePath,
        string sourceMrgPath)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Nome do mod é obrigatório.", nameof(name));
        if (!File.Exists(sourceGamePath))
            throw new FileNotFoundException("Arquivo SLUS não encontrado.", sourceGamePath);
        if (!File.Exists(sourceMrgPath))
            throw new FileNotFoundException("Arquivo WA_MRG não encontrado.", sourceMrgPath);

        var slug = Slugify(name);
        if (string.IsNullOrEmpty(slug))
            throw new ArgumentException("Nome inválido para criar pasta.", nameof(name));

        var existing = (await ListAsync()).ToList();
        if (existing.Any(m => string.Equals(m.Slug, slug, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Já existe um mod com o nome '{name}'.");

        var modFolder = Path.Combine(ModRoot, slug);
        Directory.CreateDirectory(modFolder);

        var gameFileName = Path.GetFileName(sourceGamePath);
        var mrgFileName  = Path.GetFileName(sourceMrgPath);

        var destGame = Path.Combine(modFolder, gameFileName);
        var destMrg  = Path.Combine(modFolder, mrgFileName);

        File.Copy(sourceGamePath, destGame, overwrite: true);
        File.Copy(sourceMrgPath,  destMrg,  overwrite: true);

        // Copy chartable next to the game file if it exists alongside the source
        var sourceCharTable = Path.Combine(
            Path.GetDirectoryName(sourceGamePath) ?? "",
            "chartable.tbl");
        if (File.Exists(sourceCharTable))
        {
            File.Copy(sourceCharTable,
                Path.Combine(modFolder, "chartable.tbl"),
                overwrite: true);
        }

        var mod = new Mod
        {
            Name         = name.Trim(),
            Slug         = slug,
            GameFileName = gameFileName,
            MrgFileName  = mrgFileName,
            CreatedAt    = DateTime.Now,
        };

        existing.Add(mod);
        await SaveIndexAsync(existing);

        return mod;
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
        // Collapse multiple underscores
        while (s.Contains("__")) s = s.Replace("__", "_");
        return s.Trim('_', '.');
    }
}
