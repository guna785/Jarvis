using CommunityToolkit.Maui;
using Jarvis.Contract;
using Jarvis.Services;
using Microsoft.Extensions.Logging;

namespace Jarvis;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			}).UseMauiCommunityToolkit();
#if ANDROID
        builder.Services.AddSingleton<IContinuousMicService, ContinuousMicImplementation>();
#endif
        builder.ConfigureMauiHandlers(handlers => {
#if ANDROID
            // Pierce through the Activity Background
            Microsoft.Maui.Handlers.PageHandler.Mapper.AppendToMapping("TransparentPage", (handler, view) => {
                handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
            });
#endif
        });
#if DEBUG
        builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
