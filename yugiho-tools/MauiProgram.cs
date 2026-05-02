using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using yugiho_tools.Application.Services;
using yugiho_tools.Application.UseCases;
using yugiho_tools.Domain.Interfaces;
using yugiho_tools.Infrastructure.CardDetection;
using yugiho_tools.Infrastructure.Parsing;
using yugiho_tools.Infrastructure.ScreenCapture;

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
        builder.Services.AddSingleton<IRomParser,     RomParser>();
        builder.Services.AddSingleton<IFusionEngine,  FusionEngine>();
        builder.Services.AddSingleton<IScreenCapture, WindowsScreenCapture>();
        builder.Services.AddSingleton<ICardDetector,  OpenCvCardDetector>();

        builder.Services.AddScoped<LoadRomDataUseCase>();
        builder.Services.AddScoped<GetFusionsFromHandUseCase>();
        builder.Services.AddScoped<DetectHandFromScreenUseCase>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
