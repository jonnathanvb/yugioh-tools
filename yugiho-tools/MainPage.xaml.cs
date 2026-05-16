namespace yugiho_tools;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();

#if MACCATALYST
        // Registra o scheme modimg:// no WKWebView ANTES da criação.
        // Sem isso o WKWebView ignora URLs com esse scheme e a img fica
        // em broken-image. Custom schemes precisam ser configurados na
        // WKWebViewConfiguration; depois que a view é criada, é tarde.
        blazorWebView.BlazorWebViewInitializing += (s, e) =>
        {
            e.Configuration.SetUrlSchemeHandler(
                new Platforms.MacCatalyst.ModImageSchemeHandler(),
                Platforms.MacCatalyst.ModImageSchemeHandler.Scheme);
        };
#elif WINDOWS
        // Equivalente Windows: WebView2 não aceita custom schemes em
        // runtime; usamos http://modimg.local/... via WebResourceRequested.
        // CardImage escolhe a forma da URL conforme a plataforma.
        blazorWebView.BlazorWebViewInitialized += (s, e) =>
        {
            Platforms.Windows.ModImageResourceHandler.Attach(e.WebView.CoreWebView2);
        };
#endif
    }
}
