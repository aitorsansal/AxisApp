using CommunityToolkit.Maui.Behaviors;

namespace AxisApp.Controls;

/// <summary>
/// Attached-property shortcuts for the app's "juice" pass — small, subtle motion on top of
/// otherwise-instant interactions. Two independent knobs, meant to be combined:
///
/// - PressScale: animated press-down/release-back feedback via CommunityToolkit.Maui's
///   TouchBehavior (confirmed present in the installed 13.0.0 package via reflection before
///   using it — see CLAUDE.md's "reflection, not docs" rule). Deliberately NOT used on Button
///   elements that already carry a VisualStateManager Pressed-state Scale setter (Styles.xaml's
///   base Button style, BtnPrimaryStyle, etc.) — both would fight over the same Scale property.
///   Use it on Border-based tappable rows/chips instead, which have no such VSM.
/// - Selected: a quick scale-pop (bounce) played once when the attached bool flips to true.
///   Safe on Buttons too, since it only fires after a selection change settles (well after any
///   VSM press/release has already reset Scale back to 1).
/// </summary>
public static class Juice
{
    public static readonly BindableProperty PressScaleProperty = BindableProperty.CreateAttached(
        "PressScale", typeof(double), typeof(Juice), 0d, propertyChanged: OnPressScaleChanged);

    public static void SetPressScale(BindableObject view, double value) => view.SetValue(PressScaleProperty, value);
    public static double GetPressScale(BindableObject view) => (double)view.GetValue(PressScaleProperty);

    private static void OnPressScaleChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not VisualElement element) return;
        if (newValue is not double scale || scale <= 0 || scale >= 1) return;

        element.Behaviors.Add(new TouchBehavior
        {
            PressedScale = scale,
            DefaultAnimationDuration = 120,
            PressedAnimationDuration = 80,
        });
    }

    /// <summary>Peak scale for the Selected bounce below — override per-element when the default
    /// (1.10) reads as too big, e.g. a column that also contains text (the Add Expense payer
    /// picker uses 1.05). Must be set (as a plain value, no binding needed) before Selected.</summary>
    public static readonly BindableProperty BounceScaleProperty = BindableProperty.CreateAttached(
        "BounceScale", typeof(double), typeof(Juice), 1.10);

    public static void SetBounceScale(BindableObject view, double value) => view.SetValue(BounceScaleProperty, value);
    public static double GetBounceScale(BindableObject view) => (double)view.GetValue(BounceScaleProperty);

    public static readonly BindableProperty SelectedProperty = BindableProperty.CreateAttached(
        "Selected", typeof(bool), typeof(Juice), false, propertyChanged: OnSelectedChanged);

    public static void SetSelected(BindableObject view, bool value) => view.SetValue(SelectedProperty, value);
    public static bool GetSelected(BindableObject view) => (bool)view.GetValue(SelectedProperty);

    private static void OnSelectedChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not VisualElement element) return;
        if (newValue is not bool isSelected || !isSelected) return;

        var peak = GetBounceScale(element);

        // Grow then settle back to exactly 1.0 — no spring overshoot below 1.0, which on a
        // column that also carries text read as an extra unwanted wobble rather than a clean
        // "pop." Plain CubicOut both ways instead.
        element.AbortAnimation("juiceSelectedBounce");
        var bounce = new Animation();
        bounce.Add(0.0, 0.45, new Animation(v => element.Scale = v, 1.0, peak, Easing.CubicOut));
        bounce.Add(0.45, 1.0, new Animation(v => element.Scale = v, peak, 1.0, Easing.CubicOut));
        bounce.Commit(element, "juiceSelectedBounce", 16, 200);
    }

    /// <summary>
    /// A moving pill background for a 2-way segmented control: place this Border as the FIRST
    /// child of a two-equal-column Grid (so it renders behind the two option Buttons declared
    /// after it), give it Grid.Column="0" so its measured Width equals one column, and bind
    /// SlideRight to whichever bool means "the right option is selected." It then translates
    /// itself between column 0 and column 1 instead of the options swapping Style/background
    /// instantly. The two Buttons should use a background-less "text only" style (see
    /// SegmentButtonSelectedTextOnly in Styles.xaml) so the highlight underneath shows through.
    /// </summary>
    public static readonly BindableProperty SlideRightProperty = BindableProperty.CreateAttached(
        "SlideRight", typeof(bool), typeof(Juice), false, propertyChanged: OnSlideRightChanged);

    public static void SetSlideRight(BindableObject view, bool value) => view.SetValue(SlideRightProperty, value);
    public static bool GetSlideRight(BindableObject view) => (bool)view.GetValue(SlideRightProperty);

    private static void OnSlideRightChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not VisualElement element) return;
        if (newValue is not bool slideRight) return;

        if (element.Width <= 0)
        {
            // Not laid out yet (e.g. the initial value applied before the first measure pass) —
            // snap into place with no animation the moment a real width is known.
            void OnceSized(object? s, EventArgs e)
            {
                element.SizeChanged -= OnceSized;
                element.TranslationX = slideRight ? element.Width : 0;
            }
            element.SizeChanged += OnceSized;
            return;
        }

        element.TranslateTo(slideRight ? element.Width : 0, 0, 220, Easing.CubicInOut);
    }
}
