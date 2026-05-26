using ENSP.ZD.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.ZD.Views.Pages;

public partial class EnspConfigPage : INavigableView<EnspConfigViewModel>
{
    public EnspConfigViewModel ViewModel { get; }

    public EnspConfigPage(EnspConfigViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
