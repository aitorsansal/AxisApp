using System.ComponentModel;
using System.Globalization;

namespace AxisApp.Localization;

/// <summary>
/// Single source of truth for "what language is the app in right now" — a singleton so every
/// XAML binding (via <see cref="TranslateExtension"/>) and every ViewModel-built string reads
/// the same value. TranslateExtension binds to CurrentLanguage (a plain, ordinarily-notified
/// property) through a converter rather than binding to this class's indexer directly — tried
/// the indexer approach first (raising PropertyChanged("Item[]"), the WPF convention for
/// indexer-binding refresh) and confirmed via a real Windows build that MAUI's binding engine
/// does not honor it: newly-navigated-to pages picked up a language change correctly, but
/// already-open pages never refreshed. Binding to an ordinary property is the same mechanism
/// every other binding in this app already uses successfully, so it doesn't depend on that
/// unconfirmed indexer-notification behavior at all.
/// </summary>
public sealed class LocalizationResourceManager : INotifyPropertyChanged
{
    // Captured once, at first access to this class — before Bootstrap ever mutates
    // CultureInfo.CurrentUICulture — so this reflects the device's real OS language, not
    // whatever override the app later applies on top of it. Declared before Instance
    // deliberately: static field initializers run in declaration order within one implicit
    // static constructor, and Instance's initializer constructs a LocalizationResourceManager
    // whose own field initializer reads DeviceLanguage — if DeviceLanguage were declared after
    // Instance, it would still be at its default (null) value when that construction happens.
    private static readonly string DeviceLanguage =
        AppStrings.SupportedLanguages.Contains(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            : AppStrings.English;

    public static LocalizationResourceManager Instance { get; } = new();

    private string currentLanguage = DeviceLanguage;

    private LocalizationResourceManager() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The property TranslateExtension's bindings actually watch — changing it is what
    /// drives every bound label to re-evaluate its converter and re-render.</summary>
    public string CurrentLanguage
    {
        get => currentLanguage;
        private set
        {
            if (value == currentLanguage) return;
            currentLanguage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        }
    }

    /// <summary>Null/empty override language means "follow device" — resolved to
    /// <see cref="DeviceLanguage"/> at that point, not re-resolved later, since the device's OS
    /// language can't change without an app relaunch anyway.</summary>
    public void SetLanguage(string? overrideLanguage, bool persist = true)
    {
        // Persisting the chosen override is independent of whether it changes the *resolved*
        // language below — e.g. picking "System" while the device happens to already be in
        // Spanish still needs to clear a previously-stored "es" override, even though the
        // effective language doesn't change, or the override would silently reappear on the
        // next app launch instead of actually following the device.
        if (persist)
            Microsoft.Maui.Storage.Preferences.Default.Set(AppConstants.Preferences.LanguageOverride, overrideLanguage ?? "");

        var resolved = string.IsNullOrEmpty(overrideLanguage) ? DeviceLanguage : overrideLanguage;
        if (resolved == currentLanguage) return;

        var culture = new CultureInfo(resolved);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        CurrentLanguage = resolved;
    }

    /// <summary>Reads the persisted override (if any) and applies it — called once at app
    /// startup. Must run before any page renders so the very first frame is already in the
    /// right language, not just after a delayed re-render.</summary>
    public void Bootstrap()
    {
        var stored = Microsoft.Maui.Storage.Preferences.Default.Get(AppConstants.Preferences.LanguageOverride, "");
        SetLanguage(string.IsNullOrEmpty(stored) ? null : stored, persist: false);
    }

    /// <summary>The override currently in effect, or "" for "follow device" — for the language
    /// switcher UI to show the right selection.</summary>
    public string CurrentOverride =>
        Microsoft.Maui.Storage.Preferences.Default.Get(AppConstants.Preferences.LanguageOverride, "");

    public string this[string key] => AppStrings.Get(key, currentLanguage);

    /// <summary>Uses InvariantCulture for the numeric/date formatting inside the template, not
    /// the active UI culture — matching how this app already formats every other decimal amount
    /// (AmountText/OwesText) invariantly, so "1.50" never silently becomes "1,50" depending on
    /// language and breaks round-tripping through decimal.TryParse elsewhere.</summary>
    public string Format(string key, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, AppStrings.Get(key, currentLanguage), args);
}
