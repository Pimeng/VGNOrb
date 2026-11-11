using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VGNOrb.Core.Configuration;

namespace VGNOrb.Avalonia.Views;

public sealed class HotCornerWindow : Window
{
    private readonly Border _hint;

    public HotCornerWindow()
    {
        Title = "VGN Orb Hot Corner";
        Width = 20;
        Height = 20;
        CanResize = false;
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        IsHitTestVisible = false;

        _hint = new Border
        {
            Width = 12,
            Height = 2,
            CornerRadius = new CornerRadius(1),
            Background = new SolidColorBrush(Color.Parse("#7CFF62")),
            Opacity = 0.28
        };

        Content = new Grid
        {
            Background = Brushes.Transparent,
            Children = { _hint }
        };

        ApplyCorner(HudCorner.TopRight);
    }

    /// <summary>
    /// Keeps the hint line inside the hot spot and mirrors it to the active
    /// screen corner.
    /// </summary>
    public void ApplyCorner(HudCorner corner)
    {
        _hint.HorizontalAlignment = HudPlacement.HorizontalAlignmentFor(corner);
        _hint.VerticalAlignment = HudPlacement.VerticalAlignmentFor(corner);
        _hint.Margin = HudPlacement.HintMargin(corner);
    }
}
