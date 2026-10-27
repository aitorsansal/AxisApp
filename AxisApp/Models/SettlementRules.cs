namespace AxisApp.Models;

public static class SettlementRules
{
    public const decimal MaximumAmount = AmountInput.MaximumAmount;

    public static bool TryAmount(string? text, out decimal amount)
        => AmountInput.TryParse(text, out amount);

    public static (Guid Payer, Guid Recipient) Parties(Guid me, Guid other, decimal balance) =>
        balance > 0 ? (other, me) : (me, other);

    public static string Description(string? note, string fallback) =>
        string.IsNullOrWhiteSpace(note) ? fallback : note.Trim();

    public static DateTime Timestamp(DateTime date, DateTime? original) =>
        original is { } value && value.ToLocalTime().Date == date.Date
            ? value : date.Date.ToUniversalTime();
}
