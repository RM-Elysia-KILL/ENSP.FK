using ENSP.FK.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.FK.Views.Pages;

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
