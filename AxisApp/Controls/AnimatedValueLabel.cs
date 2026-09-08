using System.Globalization;
using System.Text.RegularExpressions;

namespace AxisApp.Controls;

/// <summary>
/// Plain Label, except when a bound Text change carries a numeric run in both the old and new
/// string (e.g. "+€12.00" -> "+€18.50"), it ticks the number over ~350ms instead of snapping —
/// balances/amounts everywhere just bind Text the same way they already did to a plain Label.
/// Falls back to an instant snap (normal Label behavior) whenever either string has no number
/// (e.g. "Settled up") or the sign prefix changes (owed <-> owing) — reconstructing a signed tween
/// across that flip would read as a wrong intermediate value, not worth it for this kind of polish.
/// </summary>
public class AnimatedValueLabel : Label
{
    private static readonly Regex NumberPattern = new(@"[0-9]+([.,][0-9]+)?", RegexOptions.Compiled);

    private string? lastRenderedText;
    private bool isAnimatingInternally;

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName != nameof(Text) || isAnimatingInternally)
            return;

        var oldText = lastRenderedText;
        var newText = Text;
        lastRenderedText = newText;

        if (oldText is null || newText is null || oldText == newText)
            return;

        var oldMatch = NumberPattern.Match(oldText);
        var newMatch = NumberPattern.Match(newText);
        if (!oldMatch.Success || !newMatch.Success)
            return;

        var oldPrefix = oldText[..oldMatch.Index];
        var newPrefix = newText[..newMatch.Index];
        if (oldPrefix != newPrefix)
            return;

        if (!decimal.TryParse(oldMatch.Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var fromValue))
            return;
        if (!decimal.TryParse(newMatch.Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var toValue))
            return;

        var suffix = newText[(newMatch.Index + newMatch.Length)..];
        var parts = newMatch.Value.Split('.', ',');
        var decimalPlaces = parts.Length > 1 ? parts[1].Length : 0;

        this.AbortAnimation("animatedValue");
        var animation = new Animation(v =>
        {
            isAnimatingInternally = true;
            Text = newPrefix + v.ToString("F" + decimalPlaces, CultureInfo.InvariantCulture) + suffix;
            isAnimatingInternally = false;
        }, (double)fromValue, (double)toValue);

        animation.Commit(this, "animatedValue", 16, 350, Easing.CubicOut, (_, _) =>
        {
            isAnimatingInternally = true;
            Text = newText;
            isAnimatingInternally = false;
            lastRenderedText = newText;
        });
    }
}
