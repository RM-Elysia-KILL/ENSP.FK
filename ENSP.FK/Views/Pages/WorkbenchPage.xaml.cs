using ENSP.ZD.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.ZD.Views.Pages;

public partial class WorkbenchPage : INavigableView<WorkbenchViewModel>
{
    public WorkbenchViewModel ViewModel { get; }

    public WorkbenchPage(WorkbenchViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
