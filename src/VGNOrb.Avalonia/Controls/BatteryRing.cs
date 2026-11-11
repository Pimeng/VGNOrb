using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VGNOrb.Avalonia.Controls;

public sealed class BatteryRing : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<BatteryRing, double>(nameof(Value));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<BatteryRing, double>(nameof(StrokeThickness), 4);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<BatteryRing, IBrush?>(nameof(TrackBrush), Brushes.Transparent);

    public static readonly StyledProperty<IBrush?> ProgressBrushProperty =
        AvaloniaProperty.Register<BatteryRing, IBrush?>(nameof(ProgressBrush), Brushes.Lime);

    static BatteryRing()
    {
        AffectsRender<BatteryRing>(
            ValueProperty,
            StrokeThicknessProperty,
            TrackBrushProperty,
            ProgressBrushProperty);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? ProgressBrush
    {
        get => GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Max(0, Math.Min(Bounds.Width, Bounds.Height) / 2 - StrokeThickness / 2);
        if (radius <= 0)
        {
            return;
        }

        var trackPen = new Pen(TrackBrush ?? Brushes.Transparent, StrokeThickness);
        context.DrawEllipse(null, trackPen, center, radius, radius);

        var sweep = 360 * Math.Clamp(Value, 0, 100) / 100;
        if (sweep <= 0)
        {
            return;
        }

        var progressPen = new Pen(ProgressBrush ?? Brushes.Lime, StrokeThickness)
        {
            LineCap = PenLineCap.Round
        };

        if (sweep >= 359.99)
        {
            context.DrawEllipse(null, progressPen, center, radius, radius);
            return;
        }

        var startAngle = -Math.PI / 2;
        var endAngle = startAngle + sweep * Math.PI / 180;
        var start = new Point(
            center.X + radius * Math.Cos(startAngle),
            center.Y + radius * Math.Sin(startAngle));
        var end = new Point(
            center.X + radius * Math.Cos(endAngle),
            center.Y + radius * Math.Sin(endAngle));

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(start, isFilled: false);
            geometryContext.ArcTo(
                end,
                new Size(radius, radius),
                rotationAngle: 0,
                isLargeArc: sweep > 180,
                SweepDirection.Clockwise);
        }

        context.DrawGeometry(null, progressPen, geometry);
    }
}
