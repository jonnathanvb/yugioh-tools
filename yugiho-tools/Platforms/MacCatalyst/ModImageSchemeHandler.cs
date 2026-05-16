using Foundation;
using WebKit;

namespace yugiho_tools.Platforms.MacCatalyst;

/// <summary>
/// Handler do scheme <c>modimg://</c> usado em <see cref="WKWebView"/>
/// pra servir imagens de cartas direto do <c>FileSystem.AppDataDirectory</c>
/// sem precisar inline em <c>data:base64</c>.
///
/// <para>Motivação: BlazorWebView serializa o RenderBatch como UMA string
/// JSON ao mandar pro JS via IPC. Com 50 cartas HD inline (380 KB cada em
/// base64), a string passa de 200 MB e estoura o limite do
/// <see cref="System.Text.Json.JsonSerializer"/> (256 MB).
/// Servindo via URL scheme, o HTML só tem
/// <c>&lt;img src="modimg:///MODs/.../cards/sd/1.jpg"&gt;</c> (~80 bytes)
/// e o WKWebView busca o binário sob demanda neste handler.</para>
///
/// <para>URL format: <c>modimg:///{caminho relativo ao AppDataDirectory}</c>.
/// Ex.: <c>modimg:///MODs/Remaster_Final/cards/sd/1.jpg</c>.</para>
/// </summary>
public class ModImageSchemeHandler : NSObject, IWKUrlSchemeHandler
{
    public const string Scheme = "modimg";

    [Export("webView:startURLSchemeTask:")]
    public void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
    {
        var url = urlSchemeTask.Request.Url;
        var path = url?.Path ?? "";

        // Path traversal guard: rejeita "..", path absoluto fora do AppData
        // ou qualquer NUL byte.
        if (string.IsNullOrEmpty(path) || path.Contains("..") || path.Contains('\0'))
        {
            FailWith(urlSchemeTask, url, 400);
            return;
        }

        var root = FileSystem.AppDataDirectory;
        var absPath = Path.GetFullPath(Path.Combine(root, path.TrimStart('/')));
        if (!absPath.StartsWith(root, StringComparison.Ordinal) || !File.Exists(absPath))
        {
            FailWith(urlSchemeTask, url, 404);
            return;
        }

        try
        {
            var bytes = File.ReadAllBytes(absPath);
            var mime = GuessMime(absPath);

            var headers = new NSMutableDictionary
            {
                [new NSString("Content-Type")]    = new NSString(mime),
                [new NSString("Content-Length")]  = new NSString(bytes.Length.ToString()),
                // Cache no WebView pra carta não recarregar a cada re-render —
                // imagens dos mods são imutáveis até reimportar.
                [new NSString("Cache-Control")]   = new NSString("public, max-age=31536000, immutable"),
            };
            var response = new NSHttpUrlResponse(url!, 200, "HTTP/1.1", headers);

            urlSchemeTask.DidReceiveResponse(response);
            urlSchemeTask.DidReceiveData(NSData.FromArray(bytes));
            urlSchemeTask.DidFinish();
        }
        catch (Exception)
        {
            FailWith(urlSchemeTask, url, 500);
        }
    }

    [Export("webView:stopURLSchemeTask:")]
    public void StopUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
    {
        // Sem cancelamento explícito — leitura é síncrona e curta. WKWebView
        // exige o método existir mas pode no-op.
    }

    private static void FailWith(IWKUrlSchemeTask task, NSUrl? url, int statusCode)
    {
        try
        {
            if (url is not null)
            {
                var response = new NSHttpUrlResponse(url, statusCode, "HTTP/1.1", null);
                task.DidReceiveResponse(response);
            }
            task.DidFinish();
        }
        catch
        {
            // WKWebView pode ter cancelado a task — ignora.
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
