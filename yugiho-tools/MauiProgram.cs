using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using yugiho_tools.Application.Services;
using yugiho_tools.Application.UseCases;
using yugiho_tools.Domain.Interfaces;
using yugiho_tools.Infrastructure.CardDetection;
using yugiho_tools.Infrastructure.ModImport;
using yugiho_tools.Infrastructure.Parsing;
using yugiho_tools.Infrastructure.ScreenCapture;
using yugiho_tools.Infrastructure.Storage;

namespace yugiho_tools;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddMudServices();

        // Domain / Application
        builder.Services.AddSingleton<IMemoryCardParser,  EpsxeMemoryCardParser>();
        builder.Services.AddSingleton<IFusionEngine,      FusionEngine>();
#if WINDOWS
        builder.Services.AddSingleton<IScreenCapture, WindowsScreenCapture>();
        builder.Services.AddSingleton<IGlobalShortcutService, yugiho_tools.Infrastructure.Shortcuts.WindowsGlobalShortcutService>();
#elif MACCATALYST
        builder.Services.AddSingleton<IScreenCapture, MacScreenCapture>();
        builder.Services.AddSingleton<IGlobalShortcutService, yugiho_tools.Infrastructure.Shortcuts.MacGlobalShortcutService>();
#endif
        builder.Services.AddSingleton<ICardDetector,  OpenCvCardDetector>();
        builder.Services.AddSingleton<IModRepository, FileModRepository>();
        builder.Services.AddSingleton<AppSettings>();
        builder.Services.AddSingleton<CurrentModContext>();
        builder.Services.AddSingleton<ModCatalogService>();
        builder.Services.AddSingleton<LocalizationService>();
        builder.Services.AddSingleton<FavoritesService>();
        builder.Services.AddSingleton<LoadedModCache>();
        builder.Services.AddSingleton<SharedImagesService>();
        // Importação de mods via catálogo público + cache do data.json.
        builder.Services.AddSingleton<ExtractedDataRepository>();
        builder.Services.AddSingleton<ExtractedDataLoader>();
        builder.Services.AddSingleton<RemoteCatalogClient>();
        builder.Services.AddSingleton<ModImporter>();
        builder.Services.AddSingleton<UpdateService>();

        builder.Services.AddScoped<GetFusionsFromHandUseCase>();
        builder.Services.AddScoped<DetectHandFromScreenUseCase>();
        builder.Services.AddScoped<ListModsUseCase>();
        builder.Services.AddScoped<DeleteModUseCase>();
        builder.Services.AddScoped<ParseMemoryCardUseCase>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
