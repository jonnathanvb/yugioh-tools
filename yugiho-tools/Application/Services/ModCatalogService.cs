using System.Text.Json;

namespace yugiho_tools.Application.Services;

public record ModCatalogEntry(string Id, string Name, string Path);

public class ModCatalogService
{
    private const string Resource = "mods-catalog.json";
    private const string ImageBaseUrl = "https://www.basededatostea.xyz/img";

    private IReadOnlyList<ModCatalogEntry>? _cache;

    public async Task<IReadOnlyList<ModCatalogEntry>> GetAllAsync()
    {
        if (_cache is not null) return _cache;

        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(Resource);
            var entries = await JsonSerializer.DeserializeAsync<List<ModCatalogEntry>>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            _cache = entries ?? [];
        }
        catch
        {
            _cache = [];
        }
        return _cache;
    }

    /// <summary>
    /// Builds the full image URL template for a catalog entry.
    /// Format: <c>https://www.basededatostea.xyz/img/{path}/{id}.jpg</c>
    /// where <c>{id}</c> is replaced at render time by the card's CardId.
    /// </summary>
    public static string BuildImageUrlTemplate(ModCatalogEntry entry)
    {
        var path = (entry.Path ?? "").Trim('/');
        return $"{ImageBaseUrl}/{path}/{{id}}.jpg";
    }
}
