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

// "You and X": the three lines add up to the pairwise balance (positive = they owe me), with
// expenses involving neither of us, and parts belonging to third parties, ignored.
var me = Guid.NewGuid();
var them = Guid.NewGuid();
var other = Guid.NewGuid();
PairExpense Exp(Guid paidBy, bool settlement, int day) =>
    new(Guid.NewGuid(), paidBy, settlement, "", "food", new DateTime(2026, 9, day, 12, 0, 0, DateTimeKind.Utc));
var dinner = Exp(me, false, 19);     // I paid 32, their part 16, a third party's part 8
var taxi = Exp(them, false, 20);     // they paid, my part 5
var unrelated = Exp(other, false, 21); // neither of us paid
var payback = Exp(them, true, 22);   // they settled 11 to me
var pairExpenses = new[] { dinner, taxi, unrelated, payback };
var pairShares = new[]
{
    new PairShare(dinner.Id, me, 8), new PairShare(dinner.Id, them, 16), new PairShare(dinner.Id, other, 8),
    new PairShare(taxi.Id, me, 5), new PairShare(taxi.Id, them, 5),
    new PairShare(unrelated.Id, me, 7), new PairShare(unrelated.Id, them, 7),
    new PairShare(payback.Id, me, 11),
};
var pair = PairBreakdown.Compute(pairExpenses, pairShares, me, them);
Check(pair.PaidForThem == 16 && pair.PaidForMe == 5, "Pair lines count only the other party's part");
Check(pair.SettledToMe == 11 && pair.SettlementsNet == -11, "Settle-ups they paid me reduce what they owe");
Check(pair.Balance == 0, $"16 - 5 - 11 settles the pair: {pair.Balance}");
Check(pair.Expenses.Count == 2 && pair.Expenses[0].ExpenseId == taxi.Id && !pair.Expenses[0].IPaid,
    "Expense list excludes settlements and unrelated rows, newest first");
Console.WriteLine("Android domain regressions passed: 30,030 split scenarios, schedule boundaries, event instants and ranges.");
