namespace AxisApp.Models;

public static class ExpenseSplitMode
{
    public const string Equal = "equal";
    public const string Manual = "manual";

    public static bool IsEqual(string? mode) => mode == Equal;

    public static decimal[] AllocateEqual(decimal amount, IReadOnlyList<Guid> members,
        IReadOnlyDictionary<Guid, decimal>? saved)
    {
        if (saved is not null && members.Count > 0 && saved.Count == members.Count
            && members.All(id => saved.TryGetValue(id, out var value) && value > 0 && value * 100 == decimal.Truncate(value * 100))
            && members.Sum(id => saved[id]) == amount
            && members.Max(id => saved[id]) - members.Min(id => saved[id]) <= .01m)
            return members.Select(id => saved[id]).ToArray();
        return ExpenseMath.SplitEqually(amount, members.Count);
    }
}
