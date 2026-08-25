namespace AxisApp.Services;

/// <summary>
/// Turns a set of net balances (e.g. group_balances rows) into the minimum number of transfers
/// that settles everyone up — the standard greedy "biggest creditor vs biggest debtor" debt
/// simplification (what Tricount/Splitwise call the group's simplified settle-up view). This is
/// not a record of who originally transacted with whom — cyclic or offsetting debts net out to
/// fewer/smaller transfers than the raw history, by design. See AxisApp design discussion
/// 2026-08-25 (simplified vs pairwise balance display).
/// </summary>
public static class DebtSimplifier
{
    public readonly record struct Transfer(Guid FromMemberId, Guid ToMemberId, decimal Amount);

    private const decimal Epsilon = 0.005m;

    public static List<Transfer> Simplify(IEnumerable<(Guid MemberId, decimal Balance)> balances)
    {
        var creditors = new List<(Guid Id, decimal Amount)>();
        var debtors = new List<(Guid Id, decimal Amount)>();
        foreach (var (memberId, balance) in balances)
        {
            if (balance > Epsilon) creditors.Add((memberId, balance));
            else if (balance < -Epsilon) debtors.Add((memberId, -balance));
        }

        creditors.Sort((a, b) => b.Amount.CompareTo(a.Amount));
        debtors.Sort((a, b) => b.Amount.CompareTo(a.Amount));

        var transfers = new List<Transfer>();
        var ci = 0;
        var di = 0;
        while (ci < creditors.Count && di < debtors.Count)
        {
            var (creditorId, creditorAmount) = creditors[ci];
            var (debtorId, debtorAmount) = debtors[di];
            var settled = Math.Min(creditorAmount, debtorAmount);

            transfers.Add(new Transfer(debtorId, creditorId, decimal.Round(settled, 2)));

            creditorAmount -= settled;
            debtorAmount -= settled;
            creditors[ci] = (creditorId, creditorAmount);
            debtors[di] = (debtorId, debtorAmount);

            if (creditorAmount <= Epsilon) ci++;
            if (debtorAmount <= Epsilon) di++;
        }

        return transfers;
    }
}
