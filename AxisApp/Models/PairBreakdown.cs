namespace AxisApp.Models;

/// <summary>Input rows for <see cref="PairBreakdown"/> — plain values rather than the Postgrest
/// models, so the regression test project can compile this file on its own.</summary>
public readonly record struct PairExpense(
    Guid Id, Guid PaidByMemberId, bool IsSettlement, string Description, string Category, DateTime OccurredAt);

public readonly record struct PairShare(Guid ExpenseId, Guid MemberId, decimal AmountInGroupCurrency);

/// <summary>One expense between the two members: <see cref="IPaid"/> says whose part
/// <see cref="Amount"/> is (theirs if I paid, mine if they paid).</summary>
public sealed record PairExpenseLine(
    Guid ExpenseId, string Description, string Category, DateTime OccurredAt, bool IPaid, decimal Amount);

/// <summary>"You and X" within one group, split into the parts the pairwise balance is made of —
/// the same edges my_pairwise_balances sums (payer vs. share holder, settlements included), so
/// <see cref="Balance"/> always equals that view's balance. Positive = they owe me.</summary>
public sealed record PairBreakdown(
    decimal PaidForThem,
    decimal PaidForMe,
    decimal SettledToThem,
    decimal SettledToMe,
    IReadOnlyList<PairExpenseLine> Expenses)
{
    /// <summary>Positive = I paid them more in settle-ups than they paid me.</summary>
    public decimal SettlementsNet => SettledToThem - SettledToMe;

    public decimal Balance => PaidForThem - PaidForMe + SettlementsNet;

    public static PairBreakdown Compute(
        IEnumerable<PairExpense> expenses, IEnumerable<PairShare> shares, Guid me, Guid them)
    {
        var sharesByExpense = shares.ToLookup(s => s.ExpenseId);
        decimal paidForThem = 0, paidForMe = 0, settledToThem = 0, settledToMe = 0;
        var lines = new List<PairExpenseLine>();

        foreach (var e in expenses)
        {
            var iPaid = e.PaidByMemberId == me;
            if (!iPaid && e.PaidByMemberId != them) continue;

            var otherParty = iPaid ? them : me;
            var amount = sharesByExpense[e.Id].Where(s => s.MemberId == otherParty).Sum(s => s.AmountInGroupCurrency);
            if (amount == 0) continue;

            if (e.IsSettlement)
            {
                if (iPaid) settledToThem += amount;
                else settledToMe += amount;
                continue;
            }

            if (iPaid) paidForThem += amount;
            else paidForMe += amount;
            lines.Add(new PairExpenseLine(e.Id, e.Description, e.Category, e.OccurredAt, iPaid, amount));
        }

        return new PairBreakdown(
            paidForThem, paidForMe, settledToThem, settledToMe,
            lines.OrderByDescending(l => l.OccurredAt).ToList());
    }
}
