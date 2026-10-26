namespace AxisApp.Models;

public static class EventTimes
{
    public static bool IsPast(DateTime startsAt, DateTime now) => startsAt.ToUniversalTime() < now.ToUniversalTime();

    public static string FormatRange(DateTime startsAt, DateTime? endsAt)
    {
        var start = startsAt.ToLocalTime();
        var caption = start.ToString("ddd, MMM d, yyyy · HH:mm");
        if (endsAt is not { } endValue) return caption;
        var end = endValue.ToLocalTime();
        return $"{caption} – {end.ToString(start.Date == end.Date ? "HH:mm" : "ddd, MMM d, yyyy · HH:mm")}";
    }
}
