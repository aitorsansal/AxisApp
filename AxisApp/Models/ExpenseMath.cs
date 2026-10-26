namespace AxisApp.Models;

public static class ExpenseMath
{
    public static decimal[] SplitEqually(decimal amount, int count)
    {
        if (count <= 0) return [];
        var cents = decimal.ToInt64(decimal.Round(amount * 100, 0, MidpointRounding.AwayFromZero));
        var basis = cents / count;
        var remainder = cents % count;
        return Enumerable.Range(0, count).Select(i => (basis + (i < remainder ? 1 : 0)) / 100m).ToArray();
    }
}
