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

foreach (var (text, amount) in new[] { ("8.50", 8.50m), ("8,50", 8.50m), (" 0,01 ", .01m), ("12", 12m), ("9999999999.99", 9999999999.99m) })
    Check(SettlementRules.TryAmount(text, out var parsed) && parsed == amount, $"Settlement amount: {text}");
foreach (var text in new[] { "", " ", "0", "-1", "1.001", "1,234.56", "1.234,56", "1e3", "Infinity", "10000000000" })
    Check(!SettlementRules.TryAmount(text, out _), $"Reject invalid settlement amount: {text}");
Check(SettlementRules.Description("  ", "Settlement with Alex") == "Settlement with Alex", "Empty note saves English placeholder");
Check(SettlementRules.Description("", "Saldar deuda con Alex") == "Saldar deuda con Alex", "Empty note saves Spanish placeholder");
Check(SettlementRules.Description("  Bizum  ", "fallback") == "Bizum", "Custom note is preserved");
Check(SettlementRules.Timestamp(utcStart.ToLocalTime().Date, utcStart) == utcStart, "Unchanged repayment date preserves its instant");
foreach (var balance in new[] { 20m, -20m })
foreach (var repayment in new[] { 20m, 7.50m })
{
    var parties = SettlementRules.Parties(me, them, balance);
    var debt = Exp(balance > 0 ? me : them, false, 19);
    var settlement = Exp(parties.Payer, true, 20);
    var result = PairBreakdown.Compute([debt, settlement],
        [new PairShare(debt.Id, balance > 0 ? them : me, 20m), new PairShare(settlement.Id, parties.Recipient, repayment)], me, them);
    Check(result.Balance == Math.Sign(balance) * (20m - repayment), "Repayment reduces the correct pairwise debt");
}
Console.WriteLine("Settlement regressions passed: decimal input, limits, saved descriptions, dates and full/partial repayments in both directions.");

using (var cases = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "amount-input-cases.json"))))
{
    foreach (var sample in cases.RootElement.EnumerateArray())
    {
        var text = sample.GetProperty("text").GetString();
        var expected = sample.GetProperty("cents");
        var error = AmountInput.Parse(text, out var parsed);
        if (expected.ValueKind == System.Text.Json.JsonValueKind.Null)
        {
            Check(error != AmountInputError.None && parsed == 0, $"Reject invalid money input: {text}");
            Check((error == AmountInputError.TooLarge) == (sample.GetProperty("error").GetString() == "tooLarge"), $"Error classification: {text}");
            Check(!SettlementRules.TryAmount(text, out _), "Settlements use the same parser");
        }
        else
        {
            Check(error == AmountInputError.None && parsed * 100m == expected.GetDecimal(), $"Money input must preserve exact cents: {text}");
            Check(SettlementRules.TryAmount(text, out var settlementAmount) && settlementAmount == parsed, "Settlement parsing stays consistent");
        }
    }
}
Console.WriteLine("Shared amount-input fixtures passed: totals, shares and settlements use the same 40 cases as web.");


Check(ExpenseSplitMode.IsEqual("equal"), "Explicit Equal restores Equal");
foreach (var mode in new string?[] { null, "", "manual", "unknown" })
    Check(!ExpenseSplitMode.IsEqual(mode), "Legacy or manual intent must never be inferred");
var splitMembers = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
var savedEqual = new Dictionary<Guid, decimal>
{
    [splitMembers[0]] = 3.33m, [splitMembers[1]] = 3.34m, [splitMembers[2]] = 3.33m
};
Check(ExpenseSplitMode.AllocateEqual(10m, splitMembers, savedEqual).SequenceEqual(new[] { 3.33m, 3.34m, 3.33m }), "Opening Equal retains the saved remainder recipient");
Check(ExpenseSplitMode.AllocateEqual(10m, new[] { splitMembers[1], splitMembers[2], splitMembers[0] }, savedEqual).SequenceEqual(new[] { 3.34m, 3.33m, 3.33m }), "Member ordering preserves keyed shares");
Check(ExpenseSplitMode.AllocateEqual(12m, splitMembers, savedEqual).All(x => x == 4m), "Changing total recalculates Equal");
Check(ExpenseSplitMode.AllocateEqual(10m, splitMembers.Take(2).ToArray(), savedEqual).All(x => x == 5m), "Changing participants recalculates Equal");
Check(ExpenseSplitMode.AllocateEqual(10m, new[] { splitMembers[0], splitMembers[1], Guid.NewGuid() }, savedEqual).SequenceEqual(new[] { 3.34m, 3.33m, 3.33m }), "Replacing a participant recalculates Equal");
Check(ExpenseSplitMode.AllocateEqual(10m, splitMembers, null).SequenceEqual(new[] { 3.34m, 3.33m, 3.33m }), "Explicit Equal allocates fresh integer cents");
var unevenSaved = splitMembers.Select((id, i) => (id, value: i == 0 ? 8m : 1m)).ToDictionary(x => x.id, x => x.value);
Check(ExpenseSplitMode.AllocateEqual(10m, splitMembers, unevenSaved).SequenceEqual(new[] { 3.34m, 3.33m, 3.33m }), "Uneven saved values cannot override Equal allocation");
Console.WriteLine("Split-mode regressions passed: explicit intent, legacy fallback, saved cents, reordered members, total and participant changes.");


var editor = new ExpenseEditorState("12.00", "Dinner", 0, "food", me, false,
    new[] { new ExpenseEditorShare(me, "6.00"), new ExpenseEditorShare(them, "6,00") },
    false, new DateTime(2026, 10, 28), new DateTime(2026, 10, 28), "Monthly", null, null);
var originalEditor = ExpenseEditorSnapshot.Create(editor);
Check(ExpenseEditorSnapshot.Create(editor with { Amount = "12,00", Shares = editor.Shares.Reverse().ToArray(), Frequency = "Weekly" }) == originalEditor,
    "Amount display, participant order and hidden frequency do not dirty the form");
foreach (var changed in new[] {
    editor with { Description = "Changed" }, editor with { Amount = "13" }, editor with { Amount = "12," },
    editor with { CurrencyIndex = 1 }, editor with { Category = "other" }, editor with { Payer = them },
    editor with { Manual = true }, editor with { Shares = editor.Shares.Take(1).ToArray() },
    editor with { OccurredOn = editor.OccurredOn.AddDays(1) }, editor with { Recurring = true },
    editor with { ReceiptPath = "new.webp" }, editor with { EventId = other }
}) Check(ExpenseEditorSnapshot.Create(changed) != originalEditor, "Meaningful expense edits are protected");
var repeatingEditor = editor with { Recurring = true };
Check(ExpenseEditorSnapshot.Create(repeatingEditor with { Frequency = "Weekly" }) != ExpenseEditorSnapshot.Create(repeatingEditor), "Recurring frequency is protected");
Check(ExpenseEditorSnapshot.Create(repeatingEditor with { StartDate = editor.StartDate.AddDays(1) }) != ExpenseEditorSnapshot.Create(repeatingEditor), "Recurring start date is protected");
var editGuard = new UnsavedEditGuard();
Check(!editGuard.ShouldBlock(originalEditor, true), "Initial loading cannot produce a discard prompt");
editGuard.Capture(originalEditor);
Check(!editGuard.ShouldBlock(originalEditor, false), "Pristine values leave freely");
var changedEditor = ExpenseEditorSnapshot.Create(editor with { Description = "Changed" });
Check(editGuard.ShouldBlock(changedEditor, false), "Edited values require confirmation");
Check(editGuard.ShouldBlock(changedEditor, false), "A failed save does not clear protection");
Check(!editGuard.HasChanges(originalEditor), "Reverting values removes the dirty state");
Check(editGuard.ShouldBlock(originalEditor, true), "Pending writes/uploads block leaving even with identical values");
editGuard.MarkCommitted();
Check(!editGuard.ShouldBlock(changedEditor, true), "Confirmed successful writes leave without prompting");
editGuard.Reset(); editGuard.Capture(originalEditor);
Check(editGuard.HasChanges(changedEditor), "A fresh editor restores protection after prior successful save");
Console.WriteLine("Unsaved-edit regressions passed: persisted fields, normalization, recurrence, reverted edits, failure retention and committed navigation.");
