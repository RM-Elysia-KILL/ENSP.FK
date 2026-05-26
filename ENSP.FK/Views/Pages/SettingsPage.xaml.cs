using ENSP.ZD.ViewModels.Pages;
using System.Windows;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.ZD.Views.Pages
{
    public partial class SettingsPage : INavigableView<SettingsViewModel>
    {
        public SettingsViewModel ViewModel { get; }
        private bool _syncingPassword;

        public SettingsPage(SettingsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();

            IsVisibleChanged += (_, _) =>
            {
                if (IsVisible && !_syncingPassword)
                {
                    _syncingPassword = true;
                    ApiKeyBox.Password = ViewModel.ApiKey;
                    _syncingPassword = false;
                }
            };
        }

        private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_syncingPassword) return;
            _syncingPassword = true;
            ViewModel.ApiKey = ApiKeyBox.Password;
            _syncingPassword = false;
        }
    }
}
