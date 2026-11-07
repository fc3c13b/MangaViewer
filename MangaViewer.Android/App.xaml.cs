using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace MangaViewer.Android;

public partial class App : Application
{
	private readonly IServiceProvider _services;

	public App(IServiceProvider services)
	{
		_services = services;
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		try
		{
			var mainPage = _services.GetRequiredService<MainPage>();
			return new Window(new NavigationPage(mainPage));
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[App.CreateWindow] {ex}");
			return BuildFatalStartupWindow(ex);
		}
	}

	private static Window BuildFatalStartupWindow(Exception ex)
	{
		var message = new Label
		{
			Text = "起動中にエラーが発生しました。\n\n" + ex.Message,
			Padding = new Thickness(16),
			LineBreakMode = LineBreakMode.WordWrap,
			TextColor = Colors.White
		};

		var page = new ContentPage
		{
			Title = "起動エラー",
			BackgroundColor = Color.FromArgb("#101010"),
			Content = new ScrollView { Content = message }
		};

		return new Window(new NavigationPage(page));
	}
}
