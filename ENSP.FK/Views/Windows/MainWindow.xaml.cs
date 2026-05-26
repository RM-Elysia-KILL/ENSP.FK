using ENSP.ZD.ViewModels.Windows;
using System.Diagnostics;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ENSP.ZD.Views.Windows
{
    public partial class MainWindow : INavigationWindow
    {
        public MainWindowViewModel ViewModel { get; }

        public MainWindow(
            MainWindowViewModel viewModel,
            INavigationViewPageProvider navigationViewPageProvider,
            INavigationService navigationService
        )
        {
            var swTotal = Stopwatch.StartNew();

            ViewModel = viewModel;
            DataContext = this;

            SystemThemeWatcher.Watch(this);

            var sw = Stopwatch.StartNew();
            InitializeComponent();
            sw.Stop();
            Debug.WriteLine($"[STARTUP] MainWindow.InitializeComponent: {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            SetPageService(navigationViewPageProvider);
            navigationService.SetNavigationControl(RootNavigation);
            sw.Stop();
            Debug.WriteLine($"[STARTUP] MainWindow.SetPageService + SetNavigationControl: {sw.ElapsedMilliseconds}ms");

            swTotal.Stop();
            Debug.WriteLine($"[STARTUP] MainWindow.ctor total: {swTotal.ElapsedMilliseconds}ms");
        }

        #region INavigationWindow methods

        public INavigationView GetNavigation() => RootNavigation;

        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

        public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) => RootNavigation.SetPageProviderService(navigationViewPageProvider);

        public void ShowWindow() => Show();

        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        /// <summary>
        /// Raises the closed event.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Make sure that closing this window will begin the process of closing the application.
            Application.Current.Shutdown();
        }

        INavigationView INavigationWindow.GetNavigation() => RootNavigation;

        public void SetServiceProvider(IServiceProvider serviceProvider) => RootNavigation.SetServiceProvider(serviceProvider);
    }
}
