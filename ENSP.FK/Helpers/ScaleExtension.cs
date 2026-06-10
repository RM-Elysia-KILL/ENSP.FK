using ENSP.ZD.Services;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace ENSP.ZD.Helpers;

public class ScaleExtension : MarkupExtension
{
    public ScaleExtension() { }

    public ScaleExtension(double value)
    {
        Value = value;
    }

    public double Value { get; set; }
    public bool IsFontSize { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        double factor = IsFontSize ? UiScaleService.Instance.FontScaleFactor : UiScaleService.Instance.ScaleFactor;
        double scaled = Value * factor;

        // Detect target property type to return the correct WPF type.
        // DependencyObject properties (e.g. ColumnDefinition.Width) are DependencyProperty,
        // non-DO properties (e.g. DataGridColumn.Width) are PropertyInfo.
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget pvt)
        {
            Type? targetType = pvt.TargetProperty switch
            {
                DependencyProperty dp => dp.PropertyType,
                PropertyInfo pi => pi.PropertyType,
                _ => null
            };

            if (targetType == typeof(GridLength))
                return new GridLength(scaled);
            if (targetType == typeof(DataGridLength))
                return new DataGridLength(scaled);
        }

        return scaled;
    }
}
