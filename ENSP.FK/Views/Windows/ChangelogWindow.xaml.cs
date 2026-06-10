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

        var wa = System.Windows.SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, wa.Width * 0.35);
        Height = Math.Max(MinHeight, wa.Height * 0.55);
    }
}
