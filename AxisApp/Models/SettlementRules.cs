namespace AxisApp.Models;

public static class SettlementRules
{
    public const decimal MaximumAmount = AmountInput.MaximumAmount;

    public static bool TryAmount(string? text, string? currency, out decimal amount)
        => AmountInput.TryParse(text, currency, out amount);

    public static (Guid Payer, Guid Recipient) Parties(Guid me, Guid other, decimal balance) =>
        balance > 0 ? (other, me) : (me, other);

    /// <summary>An empty note saves an empty description, never a generated one: clients show
    /// the viewer's own localized "Settle up" label for it, so nothing is frozen in the creator's
    /// language or point of view.</summary>
    public static string Description(string? note) =>
        string.IsNullOrWhiteSpace(note) ? "" : note.Trim();

    public static DateTime Timestamp(DateTime date, DateTime? original) =>
        original is { } value && value.ToLocalTime().Date == date.Date
            ? value : date.Date.ToUniversalTime();
}
