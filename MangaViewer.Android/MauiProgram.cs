using Microsoft.Extensions.Logging;

namespace MangaViewer.Android;

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
			});

		builder.Services.AddSingleton<Services.AppSettingsService>();
		builder.Services.AddSingleton<Services.LocalStorageBrowserService>();
		builder.Services.AddSingleton<Services.SambaBrowserService>();
		builder.Services.AddSingleton<Services.ArchiveImageCacheService>();
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<Pages.SambaSettingsPage>();
		builder.Services.AddTransient<Pages.SambaFolderBrowserPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
