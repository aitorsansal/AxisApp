using AxisApp.Models;

void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

for (var count = 1; count <= 30; count++)
for (var cents = 0; cents <= 1000; cents++)
{
    var shares = ExpenseMath.SplitEqually(cents / 100m, count);
    Check(shares.Sum() == cents / 100m, "Split must conserve the total");
    Check(shares.Min() >= 0 && shares.Max() - shares.Min() <= .01m, "Split must be fair and nonnegative");
    if (cents >= count) Check(shares.All(s => s > 0), "Affordable shares must be positive");
}

var start = new DateTime(2026, 10, 26);
foreach (var frequency in new[] { "daily", "weekly", "monthly", "yearly" })
    Check(RecurringSchedule.NextDue(start, null, frequency) == start, "First occurrence must be the start date");
Check(RecurringSchedule.NextDue(start, start, "monthly") == start.AddMonths(1), "Monthly recurrence");
Check(RecurringSchedule.NextDue(new DateTime(2026, 1, 31), new DateTime(2026, 1, 31), "monthly") == new DateTime(2026, 2, 28), "Month boundary");
Check(RecurringSchedule.NextDue(new DateTime(2026, 1, 31), new DateTime(2026, 2, 28), "monthly") == new DateTime(2026, 3, 31), "Month-end anchor survives February");
Check(RecurringSchedule.NextDue(new DateTime(2026, 1, 31), new DateTime(2026, 3, 28), "monthly") == new DateTime(2026, 4, 30), "Drifted template self-heals");
Check(RecurringSchedule.NextDue(new DateTime(2024, 2, 29), new DateTime(2024, 2, 29), "yearly") == new DateTime(2025, 2, 28), "Leap year");
Check(RecurringSchedule.NextDue(new DateTime(2024, 2, 29), new DateTime(2027, 2, 28), "yearly") == new DateTime(2028, 2, 29), "Leap anchor returns");

var utcStart = new DateTime(2026, 10, 26, 22, 4, 0, DateTimeKind.Utc);
var now = utcStart.AddMinutes(1);
Check(EventTimes.IsPast(utcStart, now), "UTC event must be past");
Check(EventTimes.IsPast(utcStart.ToLocalTime(), now), "Local and UTC representations must agree");
Check(!EventTimes.IsPast(utcStart.ToLocalTime(), utcStart.AddMinutes(-1)), "Future event must remain upcoming");
Check(!EventTimes.IsPast(utcStart, utcStart), "Start instant is the upcoming boundary");
Check(EventTimes.FormatRange(utcStart, utcStart.AddDays(1)).Contains(" – "), "End time must be visible");
var localNoon = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Local);
var spanishRange = EventTimes.FormatRange(localNoon, null, new System.Globalization.CultureInfo("es-ES"));
Check(spanishRange.Contains(" 8 sept") && spanishRange.EndsWith("2026 · 12:00"), $"Spanish dates are day-first: {spanishRange}");
var englishRange = EventTimes.FormatRange(localNoon, null, new System.Globalization.CultureInfo("en-US"));
Check(englishRange == "Tue, Sep 8, 2026 · 12:00", $"English dates are month-first: {englishRange}");
var spanishShort = localNoon.ToString(DateFormats.ShortDatePattern(new System.Globalization.CultureInfo("es-ES"), includeYear: false), new System.Globalization.CultureInfo("es-ES"));
Check(spanishShort.StartsWith("8 sept"), $"Spanish short dates are day-first: {spanishShort}");
Console.WriteLine("Android domain regressions passed: 30,030 split scenarios, schedule boundaries, event instants and ranges.");
