namespace AxisApp.Models;

public static class ExpenseMath
{
    /// <summary>Splits into the currency's smallest units (cents; whole yen for JPY, KRW, ISK), the
    /// first shares taking the leftover unit.</summary>
    public static decimal[] SplitEqually(decimal amount, int count, string? currency)
    {
        if (count <= 0) return [];
        var minor = CurrencyUnits.ToMinor(amount, currency);
        var basis = minor / count;
        var remainder = minor % count;
        return Enumerable.Range(0, count)
            .Select(i => CurrencyUnits.FromMinor(basis + (i < remainder ? 1 : 0), currency)).ToArray();
    }
}
