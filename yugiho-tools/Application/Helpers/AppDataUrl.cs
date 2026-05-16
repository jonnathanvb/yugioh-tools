namespace yugiho_tools.Application.Helpers;

/// <summary>
/// Constrói URLs que o <see cref="Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebView"/>
/// resolve via handler customizado pra ler arquivos do
/// <see cref="FileSystem.AppDataDirectory"/> sob demanda — alternativa
/// ao inline <c>data:image/...;base64,...</c> que estoura o IPC do
/// BlazorWebView com mods HD (>200 MB).
///
/// <para>Por plataforma:
/// <list type="bullet">
///   <item><b>MacCatalyst</b>: <c>modimg:///caminho</c>, registrado em
///   <see cref="WebKit.WKWebViewConfiguration.SetUrlSchemeHandler"/>
///   (custom scheme nativo).</item>
///   <item><b>Windows</b>: <c>http://modimg.local/caminho</c>,
///   interceptado via <c>WebView2.WebResourceRequested</c> — WebView2
///   não permite custom schemes em runtime, então usamos um host fake.</item>
/// </list></para>
/// </summary>
public static class AppDataUrl
{
    /// <summary>Constrói a URL pra um caminho relativo ao AppDataDirectory.
    /// Aceita separadores '/' ou nativos — normaliza pra '/'.</summary>
    public static string For(string relativePath)
    {
        var clean = relativePath
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/')
            .TrimStart('/');
#if WINDOWS
        return $"http://modimg.local/{clean}";
#else
        return $"modimg:///{clean}";
#endif
    }

    /// <summary>Converte path absoluto pra URL, calculando a parte relativa
    /// a partir de <see cref="FileSystem.AppDataDirectory"/>.</summary>
    public static string FromAbsolute(string absPath)
    {
        var root = FileSystem.AppDataDirectory;
        var full = Path.GetFullPath(absPath);
        var rel = full.StartsWith(root, StringComparison.Ordinal)
            ? full[root.Length..]
            : full;
        return For(rel);
    }
}
