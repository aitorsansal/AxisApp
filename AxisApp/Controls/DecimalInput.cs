namespace AxisApp.Controls;

public static class DecimalInput
{
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.CreateAttached(
        "IsEnabled", typeof(bool), typeof(DecimalInput), false, propertyChanged: OnChanged);

    public static bool GetIsEnabled(BindableObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(BindableObject target, bool value) => target.SetValue(IsEnabledProperty, value);

    private static void OnChanged(BindableObject target, object oldValue, object newValue)
    {
        if (target is not Entry entry) return;
        entry.HandlerChanged -= Configure;
        if (newValue is not true) return;
        entry.Keyboard = Keyboard.Text;
        entry.HandlerChanged += Configure;
        Configure(entry, EventArgs.Empty);
    }

    private static void Configure(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is Entry { Handler.PlatformView: Android.Widget.EditText native })
            native.SetRawInputType(Android.Text.InputTypes.ClassNumber | Android.Text.InputTypes.NumberFlagDecimal);
#endif
    }
}
