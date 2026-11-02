using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland;

/// <summary>
/// Single-line text that scrolls when it doesn't fit, like song titles on iOS:
/// pause, glide left, loop seamlessly. Short text (or <see cref="Enabled"/> off) just ends in "…".
/// </summary>
public sealed class Marquee : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(Marquee), new PropertyMetadata("", (d, _) => ((Marquee)d).Restart()));

    /// <summary>Global switch from Settings.</summary>
    public static bool Enabled { get; set; } = true;

    const double PixelsPerSecond = 32;
    static readonly TimeSpan Pause = TimeSpan.FromSeconds(2);
    const string Spacer = "        ";

    readonly Canvas _canvas = new();
    readonly TextBlock _text = new() { TextWrapping = TextWrapping.NoWrap };
    readonly TranslateTransform _shift = new();

    public Marquee()
    {
        ClipToBounds = true;
        _text.RenderTransform = _shift;
        _canvas.Children.Add(_text);
        Content = _canvas;
        SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
                Restart();
        };
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public void Restart()
    {
        _shift.BeginAnimation(TranslateTransform.XProperty, null);
        _shift.X = 0;
        OpacityMask = null;

        _text.Width = double.NaN;
        _text.TextTrimming = TextTrimming.None;
        _text.Text = Text + Spacer;
        _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var loopWidth = _text.DesiredSize.Width;

        _text.Text = Text;
        _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textWidth = _text.DesiredSize.Width;
        _canvas.Height = _text.DesiredSize.Height;

        var available = ActualWidth;
        if (!Enabled || available <= 0 || textWidth <= available)
        {
            if (available > 0)
            {
                _text.Width = available;
                _text.TextTrimming = TextTrimming.CharacterEllipsis;
            }
            return;
        }

        // Two copies side by side so the loop has no visible jump.
        _text.Text = Text + Spacer + Text;
        var scroll = TimeSpan.FromSeconds(loopWidth / PixelsPerSecond);
        var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(Pause)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-loopWidth, KeyTime.FromTimeSpan(Pause + scroll)));
        _shift.BeginAnimation(TranslateTransform.XProperty, animation);

        // Soft fade at both edges while scrolling.
        OpacityMask = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Colors.Transparent, 0),
                new(Colors.Black, 0.06),
                new(Colors.Black, 0.94),
                new(Colors.Transparent, 1),
            },
            new Point(0, 0), new Point(1, 0));
    }
}
