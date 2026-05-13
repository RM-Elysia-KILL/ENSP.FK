using ENSP.FK.Services;
using ENSP.FK.ViewModels.Pages;
using ENSP.FK.ViewModels.Windows;
using ENSP.FK.Views.Pages;
using ENSP.FK.Views.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;

namespace ENSP.FK
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        // The.NET Generic Host provides dependency injection, configuration, logging, and other services.
        // https://docs.microsoft.com/dotnet/core/extensions/generic-host
        // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
        // https://docs.microsoft.com/dotnet/core/extensions/configuration
        // https://docs.microsoft.com/dotnet/core/extensions/logging
        private static readonly IHost _host = Host
            .CreateDefaultBuilder()
            .ConfigureAppConfiguration(c => { c.SetBasePath(Path.GetDirectoryName(AppContext.BaseDirectory)); })
            .ConfigureServices((context, services) =>
            {
                services.AddNavigationViewPageProvider();

                services.AddHostedService<ApplicationHostService>();

                // Theme manipulation
                services.AddSingleton<IThemeService, ThemeService>();

                // TaskBar manipulation
                services.AddSingleton<ITaskBarService, TaskBarService>();

                // Service containing navigation, same as INavigationWindow... but without window
                services.AddSingleton<INavigationService, NavigationService>();

                // Main window with navigation
                services.AddSingleton<INavigationWindow, MainWindow>();
                services.AddSingleton<MainWindowViewModel>();

                // Project session (shared state across pages)
                services.AddSingleton<ProjectSession>();

                // API Configuration
                services.AddSingleton<Models.ApiConfig>();

                // ENSP Services
                services.AddSingleton<TopologyParser>();
                services.AddSingleton<ConfigurationGenerator>();
                services.AddSingleton<AIConfigGenerator>();
                services.AddSingleton<ConfigExporter>();
                services.AddSingleton<SystemDiagnosticsService>();
                services.AddSingleton<VBoxDeviceService>();

                // Pages & ViewModels
                services.AddSingleton<TopologyImportPage>();
                services.AddSingleton<TopologyImportViewModel>();
                services.AddSingleton<RequirementsPage>();
                services.AddSingleton<RequirementsViewModel>();
                services.AddSingleton<ConfigOutputPage>();
                services.AddSingleton<ConfigOutputViewModel>();
                services.AddSingleton<EnspConfigPage>();
                services.AddSingleton<EnspConfigViewModel>();
                services.AddSingleton<EnspScanPage>();
                services.AddSingleton<EnspScanViewModel>();

                // Legacy pages
                services.AddSingleton<DashboardPage>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<DataPage>();
                services.AddSingleton<DataViewModel>();
                services.AddSingleton<DiagnosticsPage>();
                services.AddSingleton<DiagnosticsViewModel>();
                services.AddSingleton<SettingsPage>();
                services.AddSingleton<SettingsViewModel>();
            }).Build();

        /// <summary>
        /// Gets services.
        /// </summary>
        public static IServiceProvider Services
        {
            get { return _host.Services; }
        }

        /// <summary>
        /// Occurs when the application is loading.
        /// </summary>
        private async void OnStartup(object sender, StartupEventArgs e)
        {
            await _host.StartAsync();
        }

        /// <summary>
        /// Occurs when the application is closing.
        /// </summary>
        private async void OnExit(object sender, ExitEventArgs e)
        {
            await _host.StopAsync();

            _host.Dispose();
        }

        /// <summary>
        /// Occurs when an exception is thrown by an application but not handled.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Debug.WriteLine($"Dispatcher unhandled exception: {e.Exception}");
            var ex = e.Exception.GetBaseException();
            MessageBox.Show(
                $"未处理的异常:\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
