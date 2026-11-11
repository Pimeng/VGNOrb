using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VGNOrb.Avalonia.Controls;
using VGNOrb.Avalonia.ViewModels;

namespace VGNOrb.Avalonia.Views;

public sealed class DeviceRow : Canvas
{
    private const double RowWidth = 300;

    private static readonly FontFamily HudFont = new("GeosansLight, Segoe UI");
    private static readonly IBrush White = Brushes.White;
    private static readonly IBrush Track = new SolidColorBrush(Color.Parse("#17300B"));
    private static readonly IBrush Neon = new SolidColorBrush(Color.Parse("#3CFF00"));

    private readonly BatteryRing _ring;
    private readonly TextBlock _title;
    private readonly TextBlock _identify;
    private readonly TextBlock _percentage;
    private DeviceTileViewModel? _device;
    private bool _mirrored;

    public DeviceRow()
    {
        Width = RowWidth;
        Height = 100;
        ClipToBounds = true;
        IsVisible = false;

        _ring = new BatteryRing
        {
            Width = 80,
            Height = 80,
            StrokeThickness = 4,
            TrackBrush = Track,
            ProgressBrush = Neon
        };
        SetTop(_ring, 10);
        Children.Add(_ring);

        _title = new TextBlock
        {
            Width = 190,
            FontFamily = HudFont,
            FontSize = 26,
            Foreground = White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        SetTop(_title, 4);
        Children.Add(_title);

        _identify = new TextBlock
        {
            Width = 110,
            FontFamily = HudFont,
            FontSize = 15,
            Foreground = White,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        SetTop(_identify, 53);
        Children.Add(_identify);

        _percentage = new TextBlock
        {
            Width = 70,
            FontFamily = HudFont,
            FontSize = 34,
            Foreground = White
        };
        SetTop(_percentage, 41);
        Children.Add(_percentage);

        ApplyLayout();
    }

    /// <summary>
    /// Mirrors the row for left-side corners so the battery ring stays on the
    /// inner edge and the percentage stays next to the screen edge.
    /// </summary>
    public void SetMirrored(bool mirrored)
    {
        if (_mirrored == mirrored)
        {
            return;
        }

        _mirrored = mirrored;
        ApplyLayout();
    }

    public void Bind(DeviceTileViewModel? device)
    {
        if (ReferenceEquals(_device, device))
        {
            Update();
            return;
        }

        if (_device is not null)
        {
            _device.PropertyChanged -= OnDevicePropertyChanged;
        }

        _device = device;
        if (_device is not null)
        {
            _device.PropertyChanged += OnDevicePropertyChanged;
        }

        IsVisible = _device is not null;
        Update();
    }

    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Update();
    }

    private void ApplyLayout()
    {
        SetLeft(_ring, _mirrored ? RowWidth - 12 - 80 : 12);
        SetLeft(_title, _mirrored ? RowWidth - 96 - 190 : 96);
        SetLeft(_identify, _mirrored ? RowWidth - 100 - 110 : 100);
        SetLeft(_percentage, _mirrored ? RowWidth - 205 - 70 : 205);

        _title.TextAlignment = _mirrored ? TextAlignment.Right : TextAlignment.Left;
        _identify.TextAlignment = _mirrored ? TextAlignment.Right : TextAlignment.Left;
        _percentage.TextAlignment = _mirrored ? TextAlignment.Left : TextAlignment.Right;
    }

    private void Update()
    {
        var device = _device;
        if (device is null)
        {
            _title.Text = string.Empty;
            _identify.Text = string.Empty;
            _percentage.Text = string.Empty;
            _ring.Value = 0;
            return;
        }

        _title.Text = device.HudName;
        _identify.Text = device.IdentifyText;
        _percentage.Text = device.BatteryText;
        _percentage.FontSize = device.BatteryText == "100%" ? 30 : 34;
        _ring.Value = device.IsConnected ? device.BatteryPercent : 0;
    }
}
