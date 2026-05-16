using Microsoft.Web.WebView2.Core;

namespace yugiho_tools.Platforms.Windows;

/// <summary>
/// Lado Windows do scheme <c>modimg://</c>: configura o
/// <see cref="CoreWebView2"/> pra interceptar requests da URL custom e
/// responder com bytes do <see cref="FileSystem.AppDataDirectory"/>.
///
/// <para>WebView2 não permite registrar custom schemes em runtime via
/// CoreWebView2 (são fixed-list configurada na criação do
/// CoreWebView2Environment). Por isso usamos <c>http://modimg.local/...</c>
/// no Windows e interceptamos via <see cref="WebResourceRequested"/>.
/// CardImage gera URLs <c>modimg:///...</c> no MacCatalyst e
/// <c>http://modimg.local/...</c> aqui — o JS não enxerga diferença.</para>
///
/// <para>Hoje o ExtractedDataLoader gera o formato <c>modimg://</c>;
/// a rewrite pra forma específica do Windows é feita aqui no handler
/// quando interceptar (ou pela camada de Razor — TODO).</para>
/// </summary>
public static class ModImageResourceHandler
{
    public const string HostName = "modimg.local";

    public static void Attach(CoreWebView2 webView)
    {
        // Filter "*" intercepta todas as requests pra esse host;
        // o handler decide servir do disco ou cair pra 404.
        webView.AddWebResourceRequestedFilter(
            $"http://{HostName}/*",
            CoreWebView2WebResourceContext.All);
        webView.WebResourceRequested += OnResourceRequested;
    }

    private static void OnResourceRequested(
        object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            var uri = new Uri(e.Request.Uri);
            if (!string.Equals(uri.Host, HostName, StringComparison.OrdinalIgnoreCase))
                return;

            // Path traversal guard
            var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
            if (path.Contains("..") || path.Contains('\0'))
            {
                e.Response = ((CoreWebView2)sender!).Environment
                    .CreateWebResourceResponse(null, 400, "Bad Request", "");
                return;
            }

            var root = FileSystem.AppDataDirectory;
            var absPath = Path.GetFullPath(Path.Combine(root, path));
            if (!absPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(absPath))
            {
                e.Response = ((CoreWebView2)sender!).Environment
                    .CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }

            var bytes = File.ReadAllBytes(absPath);
            var stream = new MemoryStream(bytes);
            var mime = GuessMime(absPath);
            var headers = $"Content-Type: {mime}\nCache-Control: public, max-age=31536000, immutable";
            e.Response = ((CoreWebView2)sender!).Environment
                .CreateWebResourceResponse(stream, 200, "OK", headers);
        }
        catch
        {
            e.Response = ((CoreWebView2)sender!).Environment
                .CreateWebResourceResponse(null, 500, "Internal Error", "");
        }
    }

    private static string GuessMime(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png"               => "image/png",
            ".jpg" or ".jpeg"    => "image/jpeg",
            ".gif"               => "image/gif",
            ".webp"              => "image/webp",
            ".svg"               => "image/svg+xml",
            _                    => "application/octet-stream",
        };
}
