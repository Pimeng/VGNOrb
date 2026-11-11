using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VGNOrb.Avalonia.Controls;
using VGNOrb.Avalonia.ViewModels;

namespace VGNOrb.Avalonia.Views;

public sealed class DeviceRow : Canvas
{
    private static readonly FontFamily HudFont = new("GeosansLight, Segoe UI");
    private static readonly IBrush White = Brushes.White;
    private static readonly IBrush Track = new SolidColorBrush(Color.Parse("#17300B"));
    private static readonly IBrush Neon = new SolidColorBrush(Color.Parse("#3CFF00"));

    private readonly BatteryRing _ring;
    private readonly TextBlock _title;
    private readonly TextBlock _identify;
    private readonly TextBlock _percentage;
    private DeviceTileViewModel? _device;

    public DeviceRow()
    {
        Width = 300;
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
        SetLeft(_ring, 12);
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
        SetLeft(_title, 96);
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
        SetLeft(_identify, 100);
        SetTop(_identify, 53);
        Children.Add(_identify);

        _percentage = new TextBlock
        {
            Width = 70,
            FontFamily = HudFont,
            FontSize = 34,
            Foreground = White,
            TextAlignment = TextAlignment.Right
        };
        SetLeft(_percentage, 205);
        SetTop(_percentage, 41);
        Children.Add(_percentage);
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
