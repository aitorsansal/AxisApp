namespace AxisApp.Controls;

/// <summary>A group's appearance tag: a colored circle showing its Lucide icon glyph, or an
/// initials fallback when no icon is set (see AppConstants.GroupIcons/Group.Icon). Callers feed
/// it already-resolved values (AccentPalettes.ColorFor/TextOnAccentFor, GroupIcons.GlyphFor) —
/// this control has no idea AccentPreset or the DB exist, same division of responsibility as
/// ProfileCircle/MemberDisplay.</summary>
public partial class GroupIconCircle : ContentView
{
    public static readonly BindableProperty GlyphProperty =
        BindableProperty.Create(nameof(Glyph), typeof(string), typeof(GroupIconCircle), propertyChanged: OnGlyphChanged);

    public static readonly BindableProperty HasGlyphProperty =
        BindableProperty.Create(nameof(HasGlyph), typeof(bool), typeof(GroupIconCircle), false);

    public static readonly BindableProperty InitialsProperty =
        BindableProperty.Create(nameof(Initials), typeof(string), typeof(GroupIconCircle), "?");

    public static readonly BindableProperty IconBackgroundColorProperty =
        BindableProperty.Create(nameof(IconBackgroundColor), typeof(Color), typeof(GroupIconCircle), Colors.Transparent);

    public static readonly BindableProperty IconForegroundColorProperty =
        BindableProperty.Create(nameof(IconForegroundColor), typeof(Color), typeof(GroupIconCircle), Colors.White);

    public static readonly BindableProperty DiameterProperty =
        BindableProperty.Create(nameof(Diameter), typeof(double), typeof(GroupIconCircle), 36.0);

    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Computed from Glyph, not settable directly — see OnGlyphChanged.</summary>
    public bool HasGlyph
    {
        get => (bool)GetValue(HasGlyphProperty);
        private set => SetValue(HasGlyphProperty, value);
    }

    public string Initials
    {
        get => (string)GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value);
    }

    public Color IconBackgroundColor
    {
        get => (Color)GetValue(IconBackgroundColorProperty);
        set => SetValue(IconBackgroundColorProperty, value);
    }

    public Color IconForegroundColor
    {
        get => (Color)GetValue(IconForegroundColorProperty);
        set => SetValue(IconForegroundColorProperty, value);
    }

    public double Diameter
    {
        get => (double)GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    public GroupIconCircle()
    {
        InitializeComponent();
    }

    private static void OnGlyphChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((GroupIconCircle)bindable).HasGlyph = !string.IsNullOrEmpty((string?)newValue);
}
