using System.Windows;
using Ahnen.Core.Services;
using Ahnen.Desktop.Localization;
using Ahnen.Desktop.Services;
using Ahnen.Desktop.ViewModels;
using Ahnen.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ahnen.Desktop;

public partial class App : Application
{
	private ServiceProvider? _services;

	public static ServiceProvider BuildServices(DesktopPaths paths)
	{
		var services = new ServiceCollection();
		services.AddSingleton(paths);
		services.AddSingleton(Localizer.Instance);
		services.AddLogging();
		services.AddSingleton<ApplicationLogBuffer>();
		services.AddSingleton<ILoggerProvider, ApplicationLogProvider>();
		services.AddSingleton<PreferencesService>();
		services.AddSingleton<DocumentSession>();
		services.AddSingleton<LogViewModel>();
		services.AddSingleton<TreePageViewModel>();
		services.AddSingleton<EditPageViewModel>();
		services.AddSingleton<MainViewModel>();
		return services.BuildServiceProvider();
	}

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		_services = BuildServices(DesktopPaths.ForCurrentUser());

		var localizer = _services.GetRequiredService<Localizer>();
		localizer.SetLanguage(_services.GetRequiredService<PreferencesService>().Load().Language
			?? Localizer.DetectSystemLanguage());

		var logger = _services.GetRequiredService<ILogger<App>>();
		DispatcherUnhandledException += (_, args) =>
		{
			logger.LogError(args.Exception, "Unhandled UI exception.");
			MessageBox.Show(localizer.Format("Error_Unexpected", args.Exception.Message), localizer["AppTitle"],
				MessageBoxButton.OK, MessageBoxImage.Error);
			args.Handled = true;
		};

		new MainWindow { DataContext = _services.GetRequiredService<MainViewModel>() }.Show();
	}

	protected override void OnExit(ExitEventArgs e)
	{
		_services?.Dispose();
		base.OnExit(e);
	}
}
