using System.Windows;
using System.Windows.Media;

namespace ENSP.ZD.Services;

/// <summary>
/// Global UI scale service. Calculates a multiplier based on screen resolution
/// against a 1920x1080@96DPI design baseline, clamped to [0.85, 2.5].
/// </summary>
public class UiScaleService
{
    public const double DesignWidth = 1920.0;
    public const double DesignHeight = 1080.0;
    public const double MinScale = 0.85;
    public const double MaxScale = 2.5;

    public static UiScaleService Instance { get; } = new();

    private double _scaleFactor = 1.0;
    private double _fontScaleFactor = 1.0;

    public double ScaleFactor
    {
        get => _scaleFactor;
        private set
        {
            if (Math.Abs(_scaleFactor - value) < 0.001) return;
            _scaleFactor = value;
            ScaleFactorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double FontScaleFactor
    {
        get => _fontScaleFactor;
        private set
        {
            if (Math.Abs(_fontScaleFactor - value) < 0.001) return;
            _fontScaleFactor = value;
            ScaleFactorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ScaleFactorChanged;

    public void Recalculate(Visual? visual = null)
    {
        // SystemParameters.WorkArea is in WPF DIP (already DPI-adjusted).
        // On a 3840x2160 display at 150% scaling, WorkArea is ~2560x1352.
        // On a 1920x1080 display at 100% scaling, WorkArea is ~1920x1016.
        var wa = SystemParameters.WorkArea;
        double screenW = wa.Width;
        double screenH = wa.Height;

        // Resolution-based scale: how does the screen compare to our design baseline?
        double resScale = Math.Min(screenW / DesignWidth, screenH / DesignHeight);

        // DPI adjustment: VisualTreeHelper gives the per-monitor DPI.
        // At 100% Windows scaling: dpi=96. At 150%: dpi=144.
        // When DPI is higher, text/controls need to be larger to remain legible.
        double dpiX = 96.0;
        if (visual != null)
        {
            var dpi = VisualTreeHelper.GetDpi(visual);
            dpiX = dpi.PixelsPerInchX;
        }
        else if (Application.Current?.MainWindow != null)
        {
            var dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow);
            dpiX = dpi.PixelsPerInchX;
        }

        double dpiScale = dpiX / 96.0;

        // Combined: resolution ratio * DPI ratio, clamped
        ScaleFactor = Math.Clamp(resScale * dpiScale, MinScale, MaxScale);

        // Font scaling is gentler — sqrt of layout scale to avoid extremes
        FontScaleFactor = Math.Sqrt(ScaleFactor);
    }
}
