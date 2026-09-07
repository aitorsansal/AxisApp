using Microsoft.Maui.Controls.Shapes;

namespace AxisApp.Controls;

/// <summary>
/// A placeholder block for skeleton loading screens — a rounded bar that gently pulses opacity
/// back and forth while attached to the visual tree. Set WidthRequest/HeightRequest per use to
/// mimic the shape of the real content it stands in for (a name line, an avatar circle, etc.).
/// Deliberately a plain opacity pulse rather than a moving shimmer gradient — animating
/// Border.Background's nested GradientStop offsets isn't confirmed to repaint reliably across
/// this app's platform handlers without a real device test, whereas animating Opacity via
/// ViewExtensions/Animation on a VisualElement is a long-proven, safe MAUI primitive.
/// </summary>
public class SkeletonBox : Border
{
    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius), typeof(double), typeof(SkeletonBox), 6d, propertyChanged: OnCornerRadiusChanged);

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    private static void OnCornerRadiusChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is SkeletonBox box && newValue is double radius)
            box.StrokeShape = new RoundRectangle { CornerRadius = radius };
    }

    private bool goingToHighlight = true;

    public SkeletonBox()
    {
        StrokeThickness = 0;
        StrokeShape = new RoundRectangle { CornerRadius = CornerRadius };
        Opacity = 0.6;
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler is null)
        {
            this.AbortAnimation("skeletonPulse");
            return;
        }

        if (Background is null &&
            Application.Current?.Resources.TryGetValue("BorderSubtle", out var color) == true &&
            color is Color resolvedColor)
        {
            Background = new SolidColorBrush(resolvedColor);
        }

        Pulse();
    }

    private void Pulse()
    {
        if (Handler is null) return;

        var from = goingToHighlight ? 0.45 : 1.0;
        var to = goingToHighlight ? 1.0 : 0.45;
        goingToHighlight = !goingToHighlight;

        var animation = new Animation(v => Opacity = v, from, to, Easing.SinInOut);
        animation.Commit(this, "skeletonPulse", 16, 700, finished: (_, _) => Pulse());
    }
}
