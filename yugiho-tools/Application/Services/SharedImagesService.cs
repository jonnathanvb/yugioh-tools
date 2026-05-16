using System.IO.Compression;
using yugiho_tools.Application.Helpers;

namespace yugiho_tools.Application.Services;

/// <summary>
/// Gerencia o pacote de imagens compartilhadas (attributes / star / types)
/// distribuído fora dos MODs. O ZIP fica em
/// <c>https://blob.macstudio.tech/yugioh/files_img.zip</c> e é extraído
/// pra <c>FileSystem.AppDataDirectory/SharedImages/</c>.
///
/// Estrutura esperada após extração:
/// <code>
/// SharedImages/
///   attributes/{sd,hd,sd_mod,hd_mod}/{id}.png
///   star/{sd,hd}/{id}.png
///   types/{sd,hd}/{id}.png
/// </code>
///
/// O usuário escolhe em <see cref="AppSettings"/> qual variante (subpasta)
/// de cada categoria deve abastecer o <see cref="ExtractedAssets"/>; este
/// serviço aplica a escolha na ativação do MOD e quando a preferência muda.
/// </summary>
public class SharedImagesService
{
    public const string DefaultZipUrl =
        "https://blob.macstudio.tech/yugioh/files_img.zip";

    public const string CategoryAttributes = "attributes";
    public const string CategoryStar       = "star";
    public const string CategoryTypes      = "types";

    /// <summary>Categorias suportadas — ordem usada na UI.</summary>
    public static readonly string[] Categories =
    [
        CategoryAttributes, CategoryStar, CategoryTypes,
    ];

    private readonly AppSettings _settings;
    private readonly HttpClient  _http;

    public SharedImagesService(AppSettings settings, HttpClient? http = null)
    {
        _settings = settings;
        _http     = http ?? new HttpClient();
        // Re-aplica sempre que o usuário trocar a variante nas Settings;
        // assim a UI pega a imagem nova sem precisar re-importar o MOD.
        _settings.Changed += () =>
        {
            try { Apply(); Changed?.Invoke(); }
            catch { /* leitura best-effort — UI não quebra se faltar disco */ }
        };
    }

    public event Action? Changed;

    /// <summary>Raiz no AppData onde o ZIP é extraído.</summary>
    public string RootDir =>
        Path.Combine(FileSystem.AppDataDirectory, "SharedImages");

    /// <summary>True se já existe ao menos uma das categorias instalada.</summary>
    public bool IsInstalled =>
        Directory.Exists(RootDir) &&
        Categories.Any(c => Directory.Exists(Path.Combine(RootDir, c)));

    /// <summary>Última modificação do diretório (proxy pra "última atualização").
    /// Null se nunca instalado.</summary>
    public DateTime? LastUpdatedUtc
    {
        get
        {
            try
            {
                if (!Directory.Exists(RootDir)) return null;
                return Directory.GetLastWriteTimeUtc(RootDir);
            }
            catch { return null; }
        }
    }

    /// <summary>Lista as variantes (subpastas) presentes pra uma categoria,
    /// ordenadas alfabeticamente. Vazio se a categoria nem existe.</summary>
    public IReadOnlyList<string> GetVariants(string category)
    {
        var dir = Path.Combine(RootDir, category);
        if (!Directory.Exists(dir)) return [];
        try
        {
            return Directory.EnumerateDirectories(dir)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray()!;
        }
        catch { return []; }
    }

    /// <summary>Data URL do primeiro PNG da variante — usado no preview da
    /// tela de Settings. Null se não existir.</summary>
    public string? GetPreviewDataUrl(string category, string variant)
    {
        if (string.IsNullOrEmpty(variant)) return null;
        var dir = Path.Combine(RootDir, category, variant);
        if (!Directory.Exists(dir)) return null;
        try
        {
            var first = Directory.EnumerateFiles(dir, "*.png")
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (first is null) return null;
            var bytes = File.ReadAllBytes(first);
            return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
        }
        catch { return null; }
    }

    /// <summary>Resolve a variante salva pra uma categoria, com fallback
    /// pra primeira disponível se a preferida não existir mais (ex.: usuário
    /// removeu o pacote e baixou versão diferente).</summary>
    public string? ResolveVariant(string category)
    {
        var pref = category switch
        {
            CategoryAttributes => _settings.SharedAttributesVariant,
            CategoryStar       => _settings.SharedStarVariant,
            CategoryTypes      => _settings.SharedTypesVariant,
            _                  => null,
        };
        var available = GetVariants(category);
        if (available.Count == 0) return null;
        if (!string.IsNullOrEmpty(pref) &&
            available.Any(v => string.Equals(v, pref, StringComparison.OrdinalIgnoreCase)))
            return pref;
        return available[0];
    }

    /// <summary>Popula <see cref="ExtractedAssets"/> com as variantes
    /// escolhidas. Substitui completamente o que estiver em
    /// Attributes / Types / Star (mas preserva Names/Duelists/Guardians,
    /// que continuam vindo do per-MOD).</summary>
    public void Apply()
    {
        // Sem pacote compartilhado instalado, não toca em ExtractedAssets —
        // assim mods antigos que ainda trazem attributes/types/star no ZIP
        // continuam funcionando via fallback do ExtractedDataLoader.
        if (!IsInstalled) return;
        ApplyAttributes();
        ApplyTypes();
        ApplyStar();
    }

    private void ApplyAttributes()
    {
        var variant = ResolveVariant(CategoryAttributes);
        ExtractedAssets.ClearAttributes();
        if (variant is null) return;
        var dir = Path.Combine(RootDir, CategoryAttributes, variant);
        LoadIndexedPngs(dir, (id, url) => ExtractedAssets.SetAttribute(id, url));
    }

    private void ApplyTypes()
    {
        var variant = ResolveVariant(CategoryTypes);
        ExtractedAssets.ClearTypes();
        if (variant is null) return;
        var dir = Path.Combine(RootDir, CategoryTypes, variant);
        LoadIndexedPngs(dir, (id, url) => ExtractedAssets.SetType(id, url));
    }

    private void ApplyStar()
    {
        var variant = ResolveVariant(CategoryStar);
        ExtractedAssets.ClearStar();
        if (variant is null) return;
        // Star é só 1 ícone (0.png) hoje, mas o pacote pode evoluir —
        // pega o primeiro PNG ordenado pra funcionar mesmo se renomear.
        var dir = Path.Combine(RootDir, CategoryStar, variant);
        if (!Directory.Exists(dir)) return;
        try
        {
            var first = Directory.EnumerateFiles(dir, "*.png")
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (first is null) return;
            var url = ReadAsDataUrl(first);
            if (url is not null) ExtractedAssets.SetStar(url);
        }
        catch { /* skip */ }
    }

    private static void LoadIndexedPngs(string dir, Action<int, string> set)
    {
        if (!Directory.Exists(dir)) return;
        try
        {
            foreach (var path in Directory.EnumerateFiles(dir, "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!int.TryParse(name, out var id)) continue;
                var url = ReadAsDataUrl(path);
                if (url is not null) set(id, url);
            }
        }
        catch { /* best-effort */ }
    }

    private static string? ReadAsDataUrl(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
        }
        catch { return null; }
    }

    public record DownloadProgress(int Percent, string Message);

    /// <summary>Baixa o ZIP e extrai pra <see cref="RootDir"/>, substituindo
    /// o conteúdo anterior. Categorias do MacOSX (<c>__MACOSX/</c>) são
    /// ignoradas — vêm do zip por causa do empacotamento padrão do Finder.</summary>
    public async Task DownloadAsync(
        IProgress<DownloadProgress>? progress = null,
        string? url = null,
        CancellationToken ct = default)
    {
        url ??= DefaultZipUrl;

        progress?.Report(new(0, "Baixando pacote…"));

        // ResponseHeadersRead → recebe o stream assim que os headers
        // chegam, pra que o CopyWithProgressAsync reporte progresso real
        // durante o download. Sem isso, GetStreamAsync bufferiza tudo e
        // o usuário vê "travado" em 0% até pular pra 100%.
        using var resp = await _http.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var contentLength = resp.Content.Headers.ContentLength;
        await using var network = await resp.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream(
            contentLength is > 0 ? (int)Math.Min(contentLength.Value, int.MaxValue) : 0);
        await CopyWithProgressAsync(network, buffer, contentLength, progress, ct);
        buffer.Position = 0;

        progress?.Report(new(60, "Extraindo arquivos…"));

        // Extração em Task.Run pra não bloquear a UI durante o loop
        // sync de I/O em centenas de PNGs.
        await Task.Run(() => ExtractZip(buffer, progress, ct), ct);

        progress?.Report(new(100, "Concluído."));
        Apply();
        Changed?.Invoke();
    }

    private void ExtractZip(
        MemoryStream buffer,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        // Limpeza do diretório anterior pra não acumular variantes
        // antigas que sumiram do pacote.
        if (Directory.Exists(RootDir))
        {
            try { Directory.Delete(RootDir, recursive: true); }
            catch { /* best-effort */ }
        }
        Directory.CreateDirectory(RootDir);

        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: false);

        var rootFolder = DetectRootFolder(zip);
        int total = zip.Entries.Count;
        int done  = 0;
        int lastReportedPct = 60;

        foreach (var zentry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            done++;

            if (zentry.FullName.Contains("__MACOSX", StringComparison.Ordinal))
                continue;
            if (string.IsNullOrEmpty(zentry.Name) && zentry.FullName.EndsWith('/'))
                continue;

            var rel = StripRoot(zentry.FullName, rootFolder);
            if (string.IsNullOrEmpty(rel)) continue;

            var destPath = Path.Combine(RootDir, rel);
            var fullDest = Path.GetFullPath(destPath);
            var fullRoot = Path.GetFullPath(RootDir);
            if (!fullDest.StartsWith(fullRoot, StringComparison.Ordinal))
                continue;

            if (string.IsNullOrEmpty(zentry.Name))
            {
                Directory.CreateDirectory(fullDest);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullDest)!);
                zentry.ExtractToFile(fullDest, overwrite: true);
            }

            int p = 60 + (int)(35.0 * done / total);
            if (p != lastReportedPct)
            {
                lastReportedPct = p;
                progress?.Report(new(p, $"Extraindo… ({done}/{total})"));
            }
        }
    }

    /// <summary>Detecta se o ZIP tem um único diretório raiz comum
    /// (ex.: <c>files_img/attributes/...</c>) — aí usa esse prefixo pra
    /// extrair direto em <see cref="RootDir"/> sem aninhar mais um nível.</summary>
    private static string? DetectRootFolder(ZipArchive zip)
    {
        string? root = null;
        foreach (var e in zip.Entries)
        {
            if (e.FullName.Contains("__MACOSX", StringComparison.Ordinal)) continue;
            var idx = e.FullName.IndexOf('/');
            if (idx <= 0) return null;
            var top = e.FullName[..idx];
            if (root is null) root = top;
            else if (!string.Equals(root, top, StringComparison.Ordinal))
                return null;
        }
        return root;
    }

    private static string StripRoot(string fullName, string? root)
    {
        if (string.IsNullOrEmpty(root)) return fullName;
        var prefix = root + "/";
        return fullName.StartsWith(prefix, StringComparison.Ordinal)
            ? fullName[prefix.Length..]
            : fullName;
    }

    private static async Task CopyWithProgressAsync(
        Stream src, Stream dst,
        long? contentLength,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        int lastReportedPct = -1;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;
            int p = contentLength is > 0
                ? (int)(55.0 * total / contentLength.Value)
                : 5 + (int)(Math.Min(total, 50_000_000) / 1_000_000);
            if (p > 55) p = 55;
            if (p != lastReportedPct)
            {
                lastReportedPct = p;
                progress?.Report(new(p, $"Baixando… ({total / 1024} KB)"));
            }
        }
    }
}
