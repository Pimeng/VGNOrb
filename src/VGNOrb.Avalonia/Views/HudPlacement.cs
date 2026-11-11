using Avalonia;
using Avalonia.Layout;
using VGNOrb.Core.Configuration;

namespace VGNOrb.Avalonia.Views;

/// <summary>
/// Maps the configured screen corner to the card, hot-spot and hint geometry.
/// The card always stays flush with the two outer screen edges while the corner
/// facing into the screen keeps the 30 px radius.
/// </summary>
internal static class HudPlacement
{
    private const double InnerCornerRadius = 30;

    public static bool IsRightSide(HudCorner corner) =>
        corner is HudCorner.TopRight or HudCorner.BottomRight;

    public static bool IsBottomSide(HudCorner corner) =>
        corner is HudCorner.BottomLeft or HudCorner.BottomRight;

    public static CornerRadius CardCornerRadius(HudCorner corner) => corner switch
    {
        HudCorner.TopLeft => new CornerRadius(0, 0, InnerCornerRadius, 0),
        HudCorner.BottomRight => new CornerRadius(InnerCornerRadius, 0, 0, 0),
        HudCorner.BottomLeft => new CornerRadius(0, InnerCornerRadius, 0, 0),
        _ => new CornerRadius(0, 0, 0, InnerCornerRadius)
    };

    public static HorizontalAlignment HorizontalAlignmentFor(HudCorner corner) =>
        IsRightSide(corner) ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public static VerticalAlignment VerticalAlignmentFor(HudCorner corner) =>
        IsBottomSide(corner) ? VerticalAlignment.Bottom : VerticalAlignment.Top;

    public static Thickness HintMargin(HudCorner corner) => corner switch
    {
        HudCorner.TopLeft => new Thickness(4, 5, 0, 0),
        HudCorner.BottomRight => new Thickness(0, 0, 4, 5),
        HudCorner.BottomLeft => new Thickness(4, 0, 0, 5),
        _ => new Thickness(0, 5, 4, 0)
    };
}
