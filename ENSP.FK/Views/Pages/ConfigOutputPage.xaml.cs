using ENSP.ZD.ViewModels.Pages;
using System.Windows.Threading;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.ZD.Views.Pages;

public partial class ConfigOutputPage : INavigableView<ConfigOutputViewModel>
{
    public ConfigOutputViewModel ViewModel { get; }

    public ConfigOutputPage(ConfigOutputViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();

        ViewModel.ChatMessages.CollectionChanged += (_, _) =>
        {
            if (ViewModel.ChatMessages.Count > 0)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                {
                    try { ChatListBox.ScrollIntoView(ViewModel.ChatMessages[^1]); }
                    catch { }
                });
            }
        };
    }
}
