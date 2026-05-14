using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace yugiho_tools.Infrastructure.ModImport;

/// <summary>
/// Entrada do catálogo remoto. JSON na URL pública:
/// <c>https://blob.macstudio.tech/yugioh/catalago.json</c>
/// </summary>
public record CatalogEntry(
    [property: JsonPropertyName("id")]   string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("date")] DateTime Date,
    [property: JsonPropertyName("link")] string Link);

/// <summary>
/// Baixa e cacheia o catálogo público de mods. TTL fixo de 30 dias —
/// botão "atualizar" no UI força refresh via <see cref="RefreshAsync"/>.
/// </summary>
public class RemoteCatalogClient
{
    public const string CatalogUrl =
        "https://blob.macstudio.tech/yugioh/catalago.json";

    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    private static readonly string CacheDir =
        Path.Combine(FileSystem.AppDataDirectory, "MODs");

    private static readonly string CachePath =
        Path.Combine(CacheDir, ".catalog-cache.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly HttpClient _http;

    public RemoteCatalogClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
    }

    /// <summary>Última atualização do cache (UTC) ou <c>null</c> se não há cache.</summary>
    public DateTime? GetCacheTimestamp()
    {
        if (!File.Exists(CachePath)) return null;
        try { return File.GetLastWriteTimeUtc(CachePath); }
        catch { return null; }
    }

    public bool IsCacheExpired()
    {
        var t = GetCacheTimestamp();
        return t is null || DateTime.UtcNow - t.Value > Ttl;
    }

    /// <summary>
    /// Retorna o catálogo, baixando da rede se o cache não existe ou
    /// expirou. <paramref name="forceRefresh"/> ignora o cache.
    /// Falha de rede com cache válido → devolve cache (offline graceful).
    /// </summary>
    public async Task<IReadOnlyList<CatalogEntry>> GetAsync(
        bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && !IsCacheExpired())
        {
            var cached = TryReadCache();
            if (cached is not null) return cached;
        }

        try
        {
            var entries = await _http.GetFromJsonAsync<List<CatalogEntry>>(
                CatalogUrl, JsonOpts, ct) ?? [];
            WriteCache(entries);
            return entries;
        }
        catch
        {
            // Fallback pro cache mesmo expirado se a rede falhou.
            return TryReadCache() ?? [];
        }
    }

    /// <summary>Força download ignorando TTL.</summary>
    public Task<IReadOnlyList<CatalogEntry>> RefreshAsync(CancellationToken ct = default)
        => GetAsync(forceRefresh: true, ct);

    private static List<CatalogEntry>? TryReadCache()
    {
        if (!File.Exists(CachePath)) return null;
        try
        {
            using var fs = File.OpenRead(CachePath);
            return JsonSerializer.Deserialize<List<CatalogEntry>>(fs, JsonOpts);
        }
        catch { return null; }
    }

    private static void WriteCache(IReadOnlyList<CatalogEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            using var fs = File.Create(CachePath);
            JsonSerializer.Serialize(fs, entries, JsonOpts);
        }
        catch { /* cache é otimização; falha não é crítica */ }
    }
}
