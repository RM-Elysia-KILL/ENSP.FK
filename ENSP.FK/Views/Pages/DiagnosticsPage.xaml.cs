using ENSP.ZD.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.ZD.Views.Pages;

public partial class DiagnosticsPage : INavigableView<DiagnosticsViewModel>
{
    public DiagnosticsViewModel ViewModel { get; }

    public DiagnosticsPage(DiagnosticsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
