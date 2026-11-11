using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace VGNOrb.Avalonia.Views;

public sealed class HotCornerWindow : Window
{
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

        Content = new Grid
        {
            Background = Brushes.Transparent,
            Children =
            {
                new Border
                {
                    Width = 12,
                    Height = 2,
                    CornerRadius = new CornerRadius(1),
                    Background = new SolidColorBrush(Color.Parse("#7CFF62")),
                    Opacity = 0.28,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 5, 4, 0)
                }
            }
        };
    }
}
