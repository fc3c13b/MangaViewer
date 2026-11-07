using Android.App;
using Android.Runtime;
using Android.Util;

namespace MangaViewer.Android;

[Application]
public class MainApplication : MauiApplication
{
	private static int _handlersRegistered;
	private const string LogTag = "MangaViewer.Android";

	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
		RegisterGlobalExceptionHandlers();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	private static void RegisterGlobalExceptionHandlers()
	{
		if (Interlocked.Exchange(ref _handlersRegistered, 1) == 1)
			return;

		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			var ex = args.ExceptionObject as Exception;
			LogUnhandled("AppDomain.CurrentDomain.UnhandledException", ex ?? new Exception(args.ExceptionObject?.ToString() ?? "unknown"));
		};

		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			LogUnhandled("TaskScheduler.UnobservedTaskException", args.Exception);
			args.SetObserved();
		};

		AndroidEnvironment.UnhandledExceptionRaiser += (_, args) =>
		{
			LogUnhandled("AndroidEnvironment.UnhandledExceptionRaiser", args.Exception);
		};
	}

	private static void LogUnhandled(string source, Exception ex)
	{
		try
		{
			string message = $"[{source}] {ex}";
			Log.Error(LogTag, message);
			System.Diagnostics.Debug.WriteLine(message);
		}
		catch
		{
			// Never throw from global exception logger.
		}
	}
}
