namespace AxisApp.Services;

/// <summary>The 8 selectable accent colors (Profile's "Accent color" section). Each name is a base
/// hue picked by the user directly, not derived from anything in code.</summary>
public enum AccentPreset { Blue, Green, Red, Purple, Pink, Amber, Orange, Navy }

/// <summary>Runtime accent-color switching: everywhere the app used a fixed blue/amber accent
/// (buttons, switches, checkboxes, focus rings, sliders, nav/tab indicators, badges) now resolves
/// through <c>DynamicResource</c> against these ~35 keys, so changing them re-colors every
/// already-open page live, not just newly-navigated ones.
///
/// One ResourceDictionary instance is merged once (in Bootstrap) and never removed — SetPreset
/// mutates its existing key/value entries in place rather than removing and re-adding a whole
/// dictionary object. Tried remove-then-add first (the pattern MAUI's own light/dark theming
/// samples use) and confirmed via a real Windows build/run that it under-updates: idle-state
/// Button.BackgroundColor (set via a bare Style-level Setter, e.g. BtnPrimaryStyle in Styles.xaml)
/// never picked up the new color until a VisualState transition (hover/press) forced its Setters
/// to re-apply, even though DynamicResource resolution itself was clearly working (hover always
/// showed the correct new color). In-place key mutation raises a per-key resource-changed
/// notification instead of a coarse "a MergedDictionaries entry changed" one, which native WinUI
/// button chrome actually listens to.
///
/// Deliberately scoped to only the accent-derived ~35 keys (see AccentPalettes), not the full
/// 111-key Colors.xaml — backgrounds/surfaces/status colors (Success/Warning/Danger/Info) stay
/// fixed for every preset. See CLAUDE.md's "Theming" discussion for why: a full per-preset palette
/// needs every one of those ~111 keys hand-tuned for contrast per accent, which is far more design
/// work than an accent swap for comparable visual impact.
///
/// Values in AccentPalettes are precomputed (HSL lighten/darken off each base hue, on-accent text
/// chosen per-color by WCAG contrast, Secondary derived as a hue rotation off Primary — the same
/// relationship the original hardcoded blue/amber pair already had) rather than derived at
/// runtime, so this service has no color-math dependency at all — just a lookup + in-place
/// dictionary writes.</summary>
public sealed class ThemeService
{
    public static ThemeService Instance { get; } = new();

    private ThemeService() { }

    public AccentPreset Current { get; private set; } = AccentPreset.Blue;

    private ResourceDictionary? accentDictionary;

    /// <summary>Reads the persisted preset (if any) and applies it — called once at app startup,
    /// after InitializeComponent (Application.Current.Resources' MergedDictionaries don't exist
    /// until App.xaml has been parsed, unlike LocalizationResourceManager.Bootstrap which must run
    /// before it).</summary>
    public void Bootstrap()
    {
        accentDictionary = new ResourceDictionary();
        Application.Current!.Resources.MergedDictionaries.Add(accentDictionary);

        var stored = Microsoft.Maui.Storage.Preferences.Default.Get(AppConstants.Preferences.AccentPreset, AccentPreset.Blue.ToString());
        var preset = Enum.TryParse<AccentPreset>(stored, out var parsed) ? parsed : AccentPreset.Blue;
        Apply(preset);
    }

    /// <summary>Called from Profile's "Accent color" picker.</summary>
    public void SetPreset(AccentPreset preset)
    {
        Microsoft.Maui.Storage.Preferences.Default.Set(AppConstants.Preferences.AccentPreset, preset.ToString());
        Apply(preset);
    }

    private void Apply(AccentPreset preset)
    {
        foreach (var (key, color) in AccentPalettes.Values(preset))
            accentDictionary![key] = color;

        Current = preset;
        RefreshVisibleButtons();
    }

    /// <summary>Even with in-place dictionary mutation, an already-rendered WinUI Button's native
    /// background brush doesn't re-pull from DynamicResource on its own — confirmed by testing:
    /// Label.TextColor updates live, a page re-entered afterward shows the right color (fresh
    /// controls read the current value at creation), but a Button already on screen at the moment
    /// of the preset change stays stale until forced. Handler.UpdateValue re-pushes the current
    /// (now-correct) BackgroundColor into the native control. Scoped to the current page only —
    /// every other page already gets the right color the next time it's navigated to, so there's
    /// no need to walk pages the user isn't looking at.</summary>
    private static void RefreshVisibleButtons()
    {
        if (Shell.Current?.CurrentPage is not IVisualTreeElement page) return;

        foreach (var button in FindButtons(page))
            button.Handler?.UpdateValue(nameof(Button.BackgroundColor));
    }

    private static IEnumerable<Button> FindButtons(IVisualTreeElement element)
    {
        foreach (var child in element.GetVisualChildren())
        {
            if (child is Button button)
                yield return button;

            foreach (var nested in FindButtons(child))
                yield return nested;
        }
    }
}
