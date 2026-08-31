namespace AxisApp.Controls;

/// <summary>Resolved-name avatar: an image when the member has one, initials otherwise. Callers
/// feed it already-resolved values from Services/MemberDisplay.cs — this control has no idea
/// aliases or Storage exist, it just renders whatever it's given.</summary>
public partial class ProfileCircle : ContentView
{
    public static readonly BindableProperty ImageUrlProperty =
        BindableProperty.Create(nameof(ImageUrl), typeof(string), typeof(ProfileCircle));

    public static readonly BindableProperty InitialsProperty =
        BindableProperty.Create(nameof(Initials), typeof(string), typeof(ProfileCircle), "?");

    /// <summary>"Default", "Primary", or "Phantom" — picks AvatarCircle/AvatarCirclePrimary/
    /// AvatarCirclePhantom internally (see ProfileCircle.xaml's DataTriggers).</summary>
    public static readonly BindableProperty KindProperty =
        BindableProperty.Create(nameof(Kind), typeof(string), typeof(ProfileCircle), "Default");

    public static readonly BindableProperty DiameterProperty =
        BindableProperty.Create(nameof(Diameter), typeof(double), typeof(ProfileCircle), 36.0);

    public string? ImageUrl
    {
        get => (string?)GetValue(ImageUrlProperty);
        set => SetValue(ImageUrlProperty, value);
    }

    public string Initials
    {
        get => (string)GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value);
    }

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double Diameter
    {
        get => (double)GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    public ProfileCircle()
    {
        InitializeComponent();
    }
}
