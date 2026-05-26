using ENSP.ZD.ViewModels.Windows;
using Wpf.Ui.Controls;

namespace ENSP.ZD.Views.Windows;

public partial class DeviceConfigWindow : FluentWindow
{
    private readonly DeviceConfigWindowViewModel _vm;

    public DeviceConfigWindow(DeviceConfigWindowViewModel viewModel)
    {
        _vm = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void ConfigTree_SelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ConfigTreeNode node)
        {
            // Update IsSelected flags
            foreach (var n in _vm.ConfigTreeNodes)
                UpdateSelection(n, node);

            _vm.SelectedTreeNode = node;
        }
    }

    private static void UpdateSelection(ConfigTreeNode node, ConfigTreeNode selected)
    {
        node.IsSelected = node == selected;
        foreach (var child in node.Children)
            UpdateSelection(child, selected);
    }
}
