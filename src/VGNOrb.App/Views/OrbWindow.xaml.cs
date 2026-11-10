using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace VGNOrb.App.Views;

public partial class OrbWindow : Window
{
    public OrbWindow()
    {
        InitializeComponent();
    }

    public event EventHandler? PositionChanged;

    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        PositionChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnHideClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}
