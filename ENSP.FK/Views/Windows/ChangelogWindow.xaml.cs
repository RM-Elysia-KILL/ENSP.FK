using Wpf.Ui.Controls;
using ENSP.ZD.ViewModels.Windows;

namespace ENSP.ZD.Views.Windows;

public partial class ChangelogWindow : FluentWindow
{
    public ChangelogWindowViewModel ViewModel { get; }

    public ChangelogWindow(ChangelogWindowViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
