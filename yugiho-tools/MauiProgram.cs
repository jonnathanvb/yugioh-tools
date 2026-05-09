using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using yugiho_tools.Application.Services;
using yugiho_tools.Application.UseCases;
using yugiho_tools.Domain.Interfaces;
using yugiho_tools.Infrastructure.CardDetection;
using yugiho_tools.Infrastructure.Parsing;
using yugiho_tools.Infrastructure.ScreenCapture;
using yugiho_tools.Infrastructure.Shortcuts;
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
        builder.Services.AddSingleton<IRomParser,         RomParser>();
        builder.Services.AddSingleton<IMemoryCardParser,  EpsxeMemoryCardParser>();
        builder.Services.AddSingleton<IFusionEngine,      FusionEngine>();
        builder.Services.AddSingleton<IScreenCapture, WindowsScreenCapture>();
        builder.Services.AddSingleton<ICardDetector,  OpenCvCardDetector>();
        builder.Services.AddSingleton<IModRepository, FileModRepository>();
        builder.Services.AddSingleton<IGlobalShortcutService, WindowsGlobalShortcutService>();
        builder.Services.AddSingleton<yugiho_tools.Application.Services.AppSettings>();
        builder.Services.AddSingleton<yugiho_tools.Application.Services.CurrentModContext>();
        builder.Services.AddSingleton<yugiho_tools.Application.Services.ModCatalogService>();
        builder.Services.AddSingleton<yugiho_tools.Application.Services.LocalizationService>();
        builder.Services.AddSingleton<yugiho_tools.Application.Services.FavoritesService>();
        builder.Services.AddSingleton<yugiho_tools.Application.Services.LoadedRomCache>();
        // Extração e cache em disco do MOD (data.json em MOD/{slug}/).
        builder.Services.AddSingleton<yugiho_tools.Infrastructure.Storage.ExtractedDataRepository>();
        builder.Services.AddSingleton<yugiho_tools.Application.Services.ExtractedDataLoader>();
        builder.Services.AddSingleton<yugiho_tools.Application.Services.ModExtractor>();
        builder.Services.AddSingleton<yugiho_tools.Application.Services.LabJsonImporter>();
        builder.Services.AddSingleton<yugiho_tools.Application.Services.AnthropicTranslationService>();

        // LoadRomDataUseCase precisa ser singleton porque o LoadedRomCache
        // (também singleton) injeta dele. UseCase só depende de IRomParser
        // (singleton), então não há estado de request.
        builder.Services.AddSingleton<LoadRomDataUseCase>();
        builder.Services.AddScoped<GetFusionsFromHandUseCase>();
        builder.Services.AddScoped<DetectHandFromScreenUseCase>();
        builder.Services.AddScoped<RegisterModUseCase>();
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
