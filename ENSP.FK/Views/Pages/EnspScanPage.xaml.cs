using ENSP.ZD.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.ZD.Views.Pages;

public partial class EnspScanPage : INavigableView<EnspScanViewModel>
{
    public EnspScanViewModel ViewModel { get; }

    public EnspScanPage(EnspScanViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
