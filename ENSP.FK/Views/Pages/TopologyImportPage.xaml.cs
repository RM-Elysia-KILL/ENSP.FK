using ENSP.ZD.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.ZD.Views.Pages;

public partial class TopologyImportPage : INavigableView<TopologyImportViewModel>
{
    public TopologyImportViewModel ViewModel { get; }

    public TopologyImportPage(TopologyImportViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
