using System.IO.Compression;
using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;
using yugiho_tools.Infrastructure.Storage;

namespace yugiho_tools.Infrastructure.ModImport;

/// <summary>
/// Baixa o ZIP de uma <see cref="CatalogEntry"/> e extrai pra
/// <c>FileSystem.AppDataDirectory/MODs/{name_do_mod}/</c>.
///
/// O ZIP é montado pelo <c>yugiho-download-json</c> com a estrutura:
/// <code>
/// {name_do_mod}/
///   data.json
///   attributes/  cards/  duelists/  frames/  guardians/  star/  types/
/// </code>
/// (a pasta <c>names/</c> foi removida do pipeline — o app não precisa dela).
/// </summary>
public class ModImporter
{
    public record ImportProgress(int Percent, string Message);

    private readonly IModRepository _repo;
    private readonly HttpClient     _http;

    public ModImporter(IModRepository repo, HttpClient? http = null)
    {
        _repo = repo;
        _http = http ?? new HttpClient();
    }

    public async Task<Mod> ImportAsync(
        CatalogEntry entry,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entry.Link))
            throw new InvalidOperationException("Entry sem link de download.");

        progress?.Report(new(0, "Baixando pacote…"));

        // ResponseHeadersRead é CRÍTICO — sem isso GetStreamAsync bufferiza
        // o response inteiro em memória ANTES de retornar, e o usuário
        // só vê "0%" → "100%" sem progresso visível durante o download.
        using var resp = await _http.GetAsync(
            entry.Link, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var contentLength = resp.Content.Headers.ContentLength;
        await using var network = await resp.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream(
            contentLength is > 0 ? (int)Math.Min(contentLength.Value, int.MaxValue) : 0);
        await CopyWithProgressAsync(network, buffer, contentLength, progress, ct);
        buffer.Position = 0;

        progress?.Report(new(60, "Extraindo arquivos…"));

        // Extração rodada em Task.Run pra não travar a UI — o foreach
        // sobre zip.Entries com File.WriteAllBytes é CPU/IO síncrono e
        // sem isso o BlazorWebView congela em mods grandes (722 cartas ×
        // 4 variantes = ~3000 arquivos).
        var mod = await Task.Run(() => ExtractAndRegister(
            buffer, entry, progress, ct), ct);

        progress?.Report(new(100, "Concluído."));
        return mod;
    }

    private Mod ExtractAndRegister(
        MemoryStream buffer,
        CatalogEntry entry,
        IProgress<ImportProgress>? progress,
        CancellationToken ct)
    {
        var modsRoot = FileModRepository.GetModsRoot();
        Directory.CreateDirectory(modsRoot);

        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: false);

        // Descobre o folder name raiz dentro do ZIP. Especificação:
        // o ZIP tem 1 diretório raiz (`{name_do_mod}/`) com todo o
        // conteúdo. Se vier flat (sem prefixo), cai no slug do entry.
        var rootFolder = DetectRootFolder(zip) ?? FileModRepository.Slugify(entry.Name);

        var slug = FileModRepository.Slugify(rootFolder);
        var destRoot = Path.Combine(modsRoot, slug);

        // Reinstalação limpa: apaga conteúdo anterior pra evitar lixo
        // entre versões do mesmo mod.
        if (Directory.Exists(destRoot))
        {
            try { Directory.Delete(destRoot, recursive: true); }
            catch { /* best effort — caller verá erro se persistir */ }
        }
        Directory.CreateDirectory(destRoot);

        int total = zip.Entries.Count;
        int done  = 0;
        int lastReportedPct = 60;
        foreach (var zentry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            done++;

            // Ignora artefatos do Finder/macOS que aparecem em ZIPs
            // empacotados no Mac (e quebrariam a detecção de root).
            if (zentry.FullName.Contains("__MACOSX", StringComparison.Ordinal))
                continue;

            // Diretório vazio
            if (string.IsNullOrEmpty(zentry.Name) && zentry.FullName.EndsWith('/'))
                continue;

            // Strip do diretório raiz pra extrair o conteúdo direto em
            // {destRoot}/ (resultado: destRoot/data.json, destRoot/cards/…).
            var rel = StripRoot(zentry.FullName, rootFolder);
            if (string.IsNullOrEmpty(rel)) continue;

            // Skip da pasta names/ — não é mais usada.
            if (rel.StartsWith("names/", StringComparison.OrdinalIgnoreCase) ||
                rel.Equals("names", StringComparison.OrdinalIgnoreCase))
                continue;

            var destPath = Path.Combine(destRoot, rel);
            // Path traversal guard
            var fullDest  = Path.GetFullPath(destPath);
            var fullRoot  = Path.GetFullPath(destRoot);
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

            // Reporta a cada mudança de % pra UI suavizar mesmo com mods
            // grandes (antes era % 20 fixo — mods com 3000 entries
            // reportavam 150x, mas agora preferimos throttle por valor).
            int pct = 60 + (int)(35.0 * done / total);
            if (pct != lastReportedPct)
            {
                lastReportedPct = pct;
                progress?.Report(new(pct, $"Extraindo… ({done}/{total})"));
            }
        }

        progress?.Report(new(96, "Registrando mod…"));
        // RegisterImportedAsync é IO leve no mods.json; await sync ok.
        return _repo.RegisterImportedAsync(entry.Name, rootFolder)
            .GetAwaiter().GetResult();
    }

    private static string? DetectRootFolder(ZipArchive zip)
    {
        string? root = null;
        foreach (var e in zip.Entries)
        {
            var idx = e.FullName.IndexOf('/');
            if (idx <= 0) return null;     // arquivo na raiz → ZIP é flat
            var top = e.FullName[..idx];
            if (root is null) root = top;
            else if (!string.Equals(root, top, StringComparison.Ordinal))
                return null;               // múltiplos roots → tratar como flat
        }
        return root;
    }

    private static string StripRoot(string fullName, string root)
    {
        var prefix = root + "/";
        if (fullName.StartsWith(prefix, StringComparison.Ordinal))
            return fullName[prefix.Length..];
        return fullName;
    }

    private static async Task CopyWithProgressAsync(
        Stream src, Stream dst,
        long? contentLength,
        IProgress<ImportProgress>? progress,
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

            // Com Content-Length usa o real (0..55%). Sem, cai no
            // estimador antigo (cap em 50 MB). Throttle por % pra não
            // floodar o IProgress com 100s de reports/segundo.
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
