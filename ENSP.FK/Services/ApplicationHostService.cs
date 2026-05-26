using ENSP.ZD.Views.Pages;
using ENSP.ZD.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using Wpf.Ui;

namespace ENSP.ZD.Services
{
    /// <summary>
    /// Managed host of the application.
    /// </summary>
    public class ApplicationHostService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;

        private readonly INavigationWindow _navigationWindow;

        public ApplicationHostService(IServiceProvider serviceProvider)
        {
            var sw = Stopwatch.StartNew();
            _serviceProvider = serviceProvider;
            try
            {
                _navigationWindow = (INavigationWindow)_serviceProvider.GetService(typeof(INavigationWindow))!;
                Debug.WriteLine($"[STARTUP] MainWindow resolved OK, IsLoaded={_navigationWindow is MainWindow mw && mw.IsLoaded}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[STARTUP] FAILED to resolve INavigationWindow: {ex}");
                throw;
            }
            sw.Stop();
            Debug.WriteLine($"[STARTUP] ApplicationHostService.ctor: {sw.ElapsedMilliseconds}ms");
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Debug.WriteLine("[STARTUP] ApplicationHostService.StartAsync called");
            try
            {
                await HandleActivationAsync();
                Debug.WriteLine("[STARTUP] ApplicationHostService.StartAsync completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[STARTUP] ApplicationHostService.StartAsync FAILED: {ex}");
                throw;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
        }

        private async Task HandleActivationAsync()
        {
            var sw = Stopwatch.StartNew();
            // .NET 10.0 WPF adds Window to Application.Current.Windows during construction,
            // so we can't use Windows.OfType<T>().Any() to check if the window was shown.
            // Instead, check the IsLoaded property directly.
            var mw = _navigationWindow as MainWindow;
            bool needsShow = mw == null || !mw.IsLoaded;

#if DEBUG
            Debug.WriteLine($"[STARTUP] HandleActivationAsync — IsLoaded={mw?.IsLoaded}, needsShow={needsShow}");
#endif

            if (needsShow)
            {
                _navigationWindow.ShowWindow();
                _navigationWindow.Navigate(typeof(Views.Pages.DashboardPage));
            }

            sw.Stop();
            Debug.WriteLine($"[STARTUP] HandleActivationAsync total: {sw.ElapsedMilliseconds}ms");
            await Task.CompletedTask;
        }
    }
}
