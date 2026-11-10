using System.Windows;
using System.Windows.Media.Animation;
using VGNOrb.App.ViewModels;
using Point = System.Windows.Point;

namespace VGNOrb.App.Views;

public partial class DeviceHudWindow : Window
{
    private const double CollapsedWidth = 76;
    private const double CollapsedHeight = 48;
    private const double ExpandedWidth = 300;

    private Storyboard? _activeStoryboard;

    public DeviceHudWindow()
    {
        InitializeComponent();
    }

    public void PlayShowAnimation()
    {
        StopAnimationPreservingState();

        var targetHeight = (DataContext as OrbViewModel)?.HudHeight ?? 100;
        var storyboard = new Storyboard();

        AddShowSizeAnimation(
            storyboard,
            FrameworkElement.WidthProperty,
            HudCard.Width,
            ExpandedWidth,
            overshootPixels: 7);
        AddShowSizeAnimation(
            storyboard,
            FrameworkElement.HeightProperty,
            HudCard.Height,
            targetHeight,
            overshootPixels: 5);
        AddOpacityAnimation(
            storyboard,
            HudRoot,
            to: 1,
            milliseconds: 80,
            beginMilliseconds: 0,
            new QuadraticEase { EasingMode = EasingMode.EaseOut });
        AddOpacityAnimation(
            storyboard,
            HudContent,
            to: 1,
            milliseconds: 220,
            beginMilliseconds: 110,
            new QuadraticEase { EasingMode = EasingMode.EaseOut });

        _activeStoryboard = storyboard;
        storyboard.Begin(this, isControllable: true);
    }

    public void PlayHideAnimation(Action onCompleted)
    {
        ArgumentNullException.ThrowIfNull(onCompleted);
        StopAnimationPreservingState();

        var storyboard = new Storyboard();
        var retract = new BackEase
        {
            Amplitude = 0.25,
            EasingMode = EasingMode.EaseIn
        };

        AddOpacityAnimation(
            storyboard,
            HudContent,
            to: 0,
            milliseconds: 90,
            beginMilliseconds: 0,
            new QuadraticEase { EasingMode = EasingMode.EaseIn });
        AddSizeAnimation(storyboard, FrameworkElement.WidthProperty, CollapsedWidth, 230, retract);
        AddSizeAnimation(storyboard, FrameworkElement.HeightProperty, CollapsedHeight, 230, retract);
        AddOpacityAnimation(
            storyboard,
            HudRoot,
            to: 0,
            milliseconds: 150,
            beginMilliseconds: 70,
            new QuadraticEase { EasingMode = EasingMode.EaseIn });

        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_activeStoryboard, storyboard))
            {
                return;
            }

            _activeStoryboard = null;
            onCompleted();
        };

        _activeStoryboard = storyboard;
        storyboard.Begin(this, isControllable: true);
    }

    public void StopAnimation()
    {
        StopAnimationPreservingState();
    }

    public void ResetAnimationState()
    {
        StopAnimationPreservingState();
        HudCard.Width = CollapsedWidth;
        HudCard.Height = CollapsedHeight;
        HudContent.Opacity = 0;
        HudRoot.Opacity = 0;
    }

    private void StopAnimationPreservingState()
    {
        var width = HudCard.Width;
        var height = HudCard.Height;
        var contentOpacity = HudContent.Opacity;
        var rootOpacity = HudRoot.Opacity;
        var storyboard = _activeStoryboard;
        _activeStoryboard = null;
        storyboard?.Stop(this);

        HudCard.Width = width;
        HudCard.Height = height;
        HudContent.Opacity = contentOpacity;
        HudRoot.Opacity = rootOpacity;
    }

    private void AddShowSizeAnimation(
        Storyboard storyboard,
        DependencyProperty property,
        double from,
        double to,
        double overshootPixels)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(600)
        };

        animation.KeyFrames.Add(
            new LinearDoubleKeyFrame(from, KeyTime.FromPercent(0)));
        animation.KeyFrames.Add(
            new SplineDoubleKeyFrame(
                to + overshootPixels,
                KeyTime.FromPercent(0.68),
                new KeySpline(new Point(0.15, 0.85), new Point(0.3, 1))));
        animation.KeyFrames.Add(
            new SplineDoubleKeyFrame(
                to - 2,
                KeyTime.FromPercent(0.84),
                new KeySpline(new Point(0.3, 0.7), new Point(0.55, 1))));
        animation.KeyFrames.Add(
            new SplineDoubleKeyFrame(
                to,
                KeyTime.FromPercent(1),
                new KeySpline(new Point(0.4, 0.6), new Point(0.7, 1))));

        Storyboard.SetTarget(animation, HudCard);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        storyboard.Children.Add(animation);
    }

    private void AddSizeAnimation(
        Storyboard storyboard,
        DependencyProperty property,
        double to,
        int milliseconds,
        IEasingFunction easing)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            EasingFunction = easing
        };

        Storyboard.SetTarget(animation, HudCard);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        storyboard.Children.Add(animation);
    }

    private void AddOpacityAnimation(
        Storyboard storyboard,
        FrameworkElement target,
        double to,
        int milliseconds,
        int beginMilliseconds,
        IEasingFunction easing)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            BeginTime = TimeSpan.FromMilliseconds(beginMilliseconds),
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            EasingFunction = easing
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(FrameworkElement.OpacityProperty));
        storyboard.Children.Add(animation);
    }
}
