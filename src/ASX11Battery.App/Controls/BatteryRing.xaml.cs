using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;

namespace ASX11Battery.App.Controls;

/// <summary>
/// The circular battery readout. The arc is drawn via StrokeDashOffset animation
/// rather than a Path with recomputed geometry, keeping the animation on the
/// compositor thread for smoothness.
/// </summary>
public partial class BatteryRing : System.Windows.Controls.UserControl
{
    private const double Radius = 65;
    private const double Circumference = 2 * Math.PI * Radius;

    public BatteryRing()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += (_, _) => LayoutRing();
        Apply();
    }

    public static readonly DependencyProperty BatteryPercentProperty =
        DependencyProperty.Register(nameof(BatteryPercent), typeof(int?), typeof(BatteryRing),
            new PropertyMetadata(null, OnAnyPropertyChanged));

    public int? BatteryPercent
    {
        get => (int?)GetValue(BatteryPercentProperty);
        set => SetValue(BatteryPercentProperty, value);
    }

    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(BatteryRing),
            new PropertyMetadata(null, OnAnyPropertyChanged));

    public string? Caption
    {
        get => (string?)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public static readonly DependencyProperty ChargingProperty =
        DependencyProperty.Register(nameof(Charging), typeof(bool?), typeof(BatteryRing),
            new PropertyMetadata(null, OnAnyPropertyChanged));

    public bool? Charging
    {
        get => (bool?)GetValue(ChargingProperty);
        set => SetValue(ChargingProperty, value);
    }

    public static readonly DependencyProperty AnimationSecondsProperty =
        DependencyProperty.Register(nameof(AnimationSeconds), typeof(double), typeof(BatteryRing),
            new PropertyMetadata(0.6));

    public double AnimationSeconds
    {
        get => (double)GetValue(AnimationSecondsProperty);
        set => SetValue(AnimationSecondsProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => LayoutRing();

    private void LayoutRing()
    {
        double size = Math.Max(100, Math.Min(ActualWidth > 0 ? ActualWidth : 150, ActualHeight > 0 ? ActualHeight : 150));

        TrackEllipse.Width = size;
        TrackEllipse.Height = size;
        LevelEllipse.Width = size;
        LevelEllipse.Height = size;
        GlowEllipse.Width = size;
        GlowEllipse.Height = size;

        double radius = (size / 2) - 5;
        LevelEllipse.StrokeDashArray = new DoubleCollection(new[] { 2 * Math.PI * radius });
        TrackEllipse.StrokeDashArray = new DoubleCollection(new[] { 2 * Math.PI * radius });

        Apply();
    }

    private static void OnAnyPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((BatteryRing)d).Apply();

    private void Apply()
    {
        int? percent = BatteryPercent;
        string caption = string.IsNullOrWhiteSpace(Caption) ? UnknownCaption : Caption;

        bool charging = Charging == true;

        // Ensure UI elements are ready before animating
        if (ChargingIcon != null)
            UpdateChargingAnimation(charging && percent is not null);

        if (percent is null)
        {
            PercentText.Text = "--";
            PercentText.Foreground = (Brush)FindResource("TextMutedBrush");
            PercentSign.Visibility = Visibility.Collapsed;
            CaptionText.Text = caption;
            AnimateDash(Circumference, animate: false);
            Tint((Brush)FindResource("LevelUnknownBrush"));
            return;
        }

        int clamped = Math.Clamp(percent.Value, 0, 100);
        PercentText.Text = clamped.ToString();
        PercentText.Foreground = (Brush)FindResource("TextPrimaryBrush");
        PercentSign.Visibility = Visibility.Visible;
        CaptionText.Text = caption;

        double fraction = clamped / 100.0;
        double target = Circumference * (1 - fraction);
        AnimateDash(target, animate: AnimationSeconds > 0);

        Tint((Brush)FindResource(charging ? "LevelFullBrush" : LevelKeyFor(clamped)));
    }

    private void UpdateChargingAnimation(bool charging)
    {
        if (ChargingIcon == null) return;

        ChargingIcon.BeginAnimation(UIElement.OpacityProperty, null);
        ChargingScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ChargingScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        if (!charging)
        {
            ChargingIcon.Opacity = 0;
            GlowEllipse.BeginAnimation(UIElement.OpacityProperty, null);
            GlowEllipse.Opacity = 1;
            return;
        }

        ChargingIcon.Opacity = 1;
        var pulse = new DoubleAnimation
        {
            From = 0.85,
            To = 1.15,
            Duration = TimeSpan.FromSeconds(0.8),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        var glowPulse = new DoubleAnimation
        {
            From = 0.45,
            To = 0.9,
            Duration = TimeSpan.FromSeconds(0.8),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        ChargingScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        ChargingScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        GlowEllipse.BeginAnimation(UIElement.OpacityProperty, glowPulse);
    }

    private const string UnknownCaption = "Bateria indisponível";

    public static string LevelKeyFor(int percent) => percent switch
    {
        <= 10 => "LevelCriticalBrush",
        <= 25 => "LevelLowBrush",
        <= 75 => "LevelGoodBrush",
        _ => "LevelFullBrush",
    };

    private void Tint(WpfBrush level)
    {
        LevelEllipse.Stroke = level;
        GlowStop.Color = level is SolidColorBrush solid ? solid.Color : WpfColors.Transparent;
    }

    private void AnimateDash(double target, bool animate)
    {
        var animation = new DoubleAnimation
        {
            To = target,
            Duration = new Duration(TimeSpan.FromSeconds(animate ? AnimationSeconds : 0)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        LevelRotate.Angle = -90;
        LevelEllipse.StrokeEndLineCap = PenLineCap.Round;
        LevelEllipse.StrokeStartLineCap = PenLineCap.Round;

        if (target >= Circumference - 0.5)
        {
            LevelEllipse.StrokeStartLineCap = PenLineCap.Flat;
            LevelEllipse.StrokeEndLineCap = PenLineCap.Flat;
        }

        LevelEllipse.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
        LevelEllipse.StrokeDashOffset = animate ? LevelEllipse.StrokeDashOffset : target;

        if (animate)
            LevelEllipse.BeginAnimation(Shape.StrokeDashOffsetProperty, animation);
    }
}