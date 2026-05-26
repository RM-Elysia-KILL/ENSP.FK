using ENSP.ZD.ViewModels.Pages;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.ZD.Views.Pages;

public partial class TopologyGraphPage : INavigableView<TopologyGraphViewModel>
{
    public TopologyGraphViewModel ViewModel { get; }

    public TopologyGraphPage(TopologyGraphViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    // Zoom state
    private double _currentZoom = 1.0;
    private const double MinZoom = 0.3;
    private const double MaxZoom = 3.0;

    private void TopologyCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!ViewModel.HasTopology) return;

        var mousePos = e.GetPosition(TopologyCanvas);
        double oldZoom = _currentZoom;
        double zoomDelta = e.Delta > 0 ? 1.15 : 0.87;
        _currentZoom = Math.Clamp(oldZoom * zoomDelta, MinZoom, MaxZoom);

        // Adjust pan to zoom around mouse position
        double scaleChange = _currentZoom / oldZoom;
        double offsetX = mousePos.X - PanTransform.X;
        double offsetY = mousePos.Y - PanTransform.Y;
        PanTransform.X = mousePos.X - offsetX * scaleChange;
        PanTransform.Y = mousePos.Y - offsetY * scaleChange;

        ZoomTransform.ScaleX = _currentZoom;
        ZoomTransform.ScaleY = _currentZoom;
    }

    // Drag state
    private bool _isDragging;
    private TopologyNode? _draggedNode;
    private Point _dragOffset;

    // Pan state
    private bool _isPanning;
    private Point _panStart;

    private void TopologyCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ViewModel.HasTopology) return;

        var mousePos = e.GetPosition(TopologyCanvas);

        var hitNode = FindNodeAtPosition(mousePos);
        if (hitNode != null)
        {
            _isDragging = true;
            _draggedNode = hitNode;
            _dragOffset = new Point(mousePos.X - hitNode.X, mousePos.Y - hitNode.Y);
            TopologyCanvas.CaptureMouse();
        }
    }

    private void TopologyCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ViewModel.HasTopology) return;

        _isPanning = true;
        _panStart = e.GetPosition(CanvasHost);
        TopologyCanvas.CaptureMouse();
    }

    private void TopologyCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging && _draggedNode != null)
        {
            var mousePos = e.GetPosition(TopologyCanvas);
            _draggedNode.X = mousePos.X - _dragOffset.X;
            _draggedNode.Y = mousePos.Y - _dragOffset.Y;
            UpdateLinkEndpoints();
        }
        else if (_isPanning)
        {
            var mousePos = e.GetPosition(CanvasHost);
            var delta = mousePos - _panStart;
            PanTransform.X += delta.X;
            PanTransform.Y += delta.Y;
            _panStart = mousePos;
        }
    }

    private void TopologyCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        _draggedNode = null;
        TopologyCanvas.ReleaseMouseCapture();
    }

    private void TopologyCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        TopologyCanvas.ReleaseMouseCapture();
    }

    private void UpdateLinkEndpoints()
    {
        var nodeDict = ViewModel.Nodes.ToDictionary(n => n.DeviceName);
        foreach (var link in ViewModel.LinkVms)
        {
            if (nodeDict.TryGetValue(link.SourceDevice, out var src) &&
                nodeDict.TryGetValue(link.TargetDevice, out var tgt))
            {
                var (x1, y1, x2, y2) = ComputeEdgeEndpoints(src, tgt, link.OffsetX, link.OffsetY);
                link.X1 = x1;
                link.Y1 = y1;
                link.X2 = x2;
                link.Y2 = y2;
            }
        }
    }

    private static (double x1, double y1, double x2, double y2) ComputeEdgeEndpoints(
        TopologyNode src, TopologyNode tgt, double offsetX, double offsetY)
    {
        double srcCx = src.X + src.NodeWidth / 2;
        double srcCy = src.Y + src.NodeHeight / 2;
        double tgtCx = tgt.X + tgt.NodeWidth / 2;
        double tgtCy = tgt.Y + tgt.NodeHeight / 2;

        double dx = tgtCx - srcCx;
        double dy = tgtCy - srcCy;
        double absDx = Math.Abs(dx);
        double absDy = Math.Abs(dy);

        if (absDx < 0.01 && absDy < 0.01)
            return (srcCx + offsetX, srcCy + offsetY, tgtCx + offsetX, tgtCy + offsetY);

        double hwSrc = src.NodeWidth / 2;
        double hhSrc = src.NodeHeight / 2;
        double hwTgt = tgt.NodeWidth / 2;
        double hhTgt = tgt.NodeHeight / 2;

        // Ratio of center-to-center distance to reach each rectangle edge
        double tSrc = Math.Min(
            absDx > 0.01 ? hwSrc / absDx : double.MaxValue,
            absDy > 0.01 ? hhSrc / absDy : double.MaxValue);
        double tTgt = Math.Min(
            absDx > 0.01 ? hwTgt / absDx : double.MaxValue,
            absDy > 0.01 ? hhTgt / absDy : double.MaxValue);

        // Clamp if rectangles overlap
        if (tSrc + tTgt > 1)
        {
            double scale = 1 / (tSrc + tTgt);
            tSrc *= scale;
            tTgt *= scale;
        }

        double x1 = srcCx + dx * tSrc + offsetX;
        double y1 = srcCy + dy * tSrc + offsetY;
        double x2 = tgtCx - dx * tTgt + offsetX;
        double y2 = tgtCy - dy * tTgt + offsetY;

        return (x1, y1, x2, y2);
    }

    private TopologyNode? FindNodeAtPosition(Point pos)
    {
        foreach (var node in ViewModel.Nodes.Reverse())
        {
            if (pos.X >= node.X && pos.X <= node.X + node.NodeWidth &&
                pos.Y >= node.Y && pos.Y <= node.Y + node.NodeHeight)
            {
                return node;
            }
        }
        return null;
    }
}
