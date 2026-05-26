using ENSP.ZD.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.ZD.Views.Pages;

public partial class RequirementsPage : INavigableView<RequirementsViewModel>
{
    public RequirementsViewModel ViewModel { get; }

    public RequirementsPage(RequirementsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
