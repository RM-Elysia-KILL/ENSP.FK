using ENSP.ZD.Services;
using ENSP.ZD.ViewModels.Pages;
using ENSP.ZD.ViewModels.Windows;
using ENSP.ZD.Views.Pages;
using ENSP.ZD.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;

namespace ENSP.ZD
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        // Bare HostBuilder — CreateDefaultBuilder pulls in 20+ unused assemblies
        // (Console/Debug/EventLog/EventSource loggers, JSON/XML/env/CLI config sources,
        // user secrets, file globbing) that add ~3.5s to debug startup.
        private static readonly IHost _host = new HostBuilder()
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
                services.AddSingleton<DeviceIconService>(sp =>
                {
                    var config = sp.GetRequiredService<Models.ApiConfig>();
                    return new DeviceIconService(config.EnspPath);
                });
                services.AddSingleton<TopologyParser>();
                services.AddSingleton<ConfigurationGenerator>();
                services.AddSingleton<AIConfigGenerator>();
                services.AddSingleton<ConfigExporter>();
                services.AddSingleton<SystemDiagnosticsService>();
                services.AddSingleton<VBoxDeviceService>();
                services.AddSingleton<LogService>();
                services.AddSingleton<ImageRecognitionService>();
                services.AddSingleton<EnspGuiAutomationService>();
                services.AddSingleton<DeviceStartupService>(sp =>
                {
                    var guiAuto = sp.GetRequiredService<EnspGuiAutomationService>();
                    return new DeviceStartupService(guiAuto);
                });

                // Device connection management (state machine)
                services.AddSingleton<DeviceConnectionManager>();

                // Device config popup
                services.AddTransient<DeviceConfigWindowViewModel>();
                services.AddTransient<DeviceConfigWindow>();

                // Changelog popup
                services.AddTransient<ChangelogWindowViewModel>();
                services.AddTransient<ChangelogWindow>();

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
                services.AddSingleton<TopologyGraphPage>();
                services.AddSingleton<TopologyGraphViewModel>();

                // Workbench (consolidated single-page workflow)
                services.AddSingleton<WorkbenchPage>();
                services.AddSingleton<WorkbenchViewModel>();

                // Legacy pages
                services.AddSingleton<DashboardPage>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<DataPage>();
                services.AddSingleton<DataViewModel>();
                services.AddSingleton<DiagnosticsPage>();
                services.AddSingleton<DiagnosticsViewModel>();
                services.AddSingleton<SettingsPage>();
                services.AddSingleton<SettingsViewModel>(sp =>
                {
                    var config = sp.GetRequiredService<Models.ApiConfig>();
                    var ai = sp.GetRequiredService<AIConfigGenerator>();
                    var img = sp.GetRequiredService<ImageRecognitionService>();
                    var icons = sp.GetRequiredService<DeviceIconService>();
                    return new SettingsViewModel(config, ai, img, icons);
                });
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
            var sw = Stopwatch.StartNew();
            await _host.StartAsync();
            sw.Stop();
            Debug.WriteLine($"[STARTUP] Host.StartAsync → window visible: {sw.ElapsedMilliseconds}ms");

            // Show changelog on first launch after update
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                await Dispatcher.InvokeAsync(ShowChangelogIfNewVersion);
            });
        }

        private static void ShowChangelogIfNewVersion()
        {
            try
            {
                var apiConfig = Services.GetRequiredService<Models.ApiConfig>();
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
                if (apiConfig.LastSeenVersion == currentVersion) return;

                var window = Services.GetRequiredService<Views.Windows.ChangelogWindow>();
                window.Owner = System.Windows.Application.Current.MainWindow;
                window.ShowDialog();

                apiConfig.LastSeenVersion = currentVersion;
                apiConfig.Save();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Changelog] Failed to show: {ex.Message}");
            }
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
