using Android.Animation;
using AView = Android.Views.View;

namespace AxisApp.Controls;

public sealed class PressScaleBehavior(double pressedScale) : PlatformBehavior<VisualElement, AView>
{
    private StateListAnimator? previousAnimator;
    private StateListAnimator? animator;

    protected override void OnAttachedTo(VisualElement bindable, AView platformView)
    {
        base.OnAttachedTo(bindable, platformView);
        previousAnimator = platformView.StateListAnimator;
        animator = new StateListAnimator();
        var pressed = new AnimatorSet();
        pressed.PlayTogether(
            ObjectAnimator.OfFloat(platformView, "scaleX", (float)pressedScale),
            ObjectAnimator.OfFloat(platformView, "scaleY", (float)pressedScale));
        pressed.SetDuration(80);
        var released = new AnimatorSet();
        released.PlayTogether(
            ObjectAnimator.OfFloat(platformView, "scaleX", 1f),
            ObjectAnimator.OfFloat(platformView, "scaleY", 1f));
        released.SetDuration(120);
        animator.AddState([global::Android.Resource.Attribute.StatePressed], pressed);
        animator.AddState([], released);
        platformView.StateListAnimator = animator;
    }

    protected override void OnDetachedFrom(VisualElement bindable, AView platformView)
    {
        platformView.StateListAnimator = previousAnimator;
        platformView.ScaleX = 1;
        platformView.ScaleY = 1;
        animator?.Dispose();
        animator = null;
        previousAnimator = null;
        base.OnDetachedFrom(bindable, platformView);
    }

}
