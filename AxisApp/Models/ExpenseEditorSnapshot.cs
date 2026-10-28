using System.Globalization;
using System.Text.Json;

namespace AxisApp.Models;

public sealed record ExpenseEditorShare(Guid MemberId, string Amount);

public sealed record ExpenseEditorState(
    string Amount, string Description, int CurrencyIndex, string Category, Guid? Payer,
    bool Manual, IReadOnlyList<ExpenseEditorShare> Shares, bool Recurring,
    DateTime OccurredOn, DateTime StartDate, string Frequency, string? ReceiptPath, Guid? EventId);

public static class ExpenseEditorSnapshot
{
    private static string Money(string text) => AmountInput.TryParse(text, out var amount)
        ? "amount:" + amount.ToString("0.##", CultureInfo.InvariantCulture)
        : "invalid:" + text;

    public static string Create(ExpenseEditorState state) => JsonSerializer.Serialize(new
    {
        Amount = Money(state.Amount), state.Description, state.CurrencyIndex, state.Category, state.Payer,
        state.Manual,
        Shares = state.Shares.OrderBy(s => s.MemberId).Select(s => new { s.MemberId, Amount = Money(s.Amount) }),
        state.Recurring,
        Date = (state.Recurring ? state.StartDate : state.OccurredOn).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Frequency = state.Recurring ? state.Frequency : null,
        ReceiptPath = state.Recurring ? null : state.ReceiptPath,
        EventId = state.Recurring ? null : state.EventId
    });
}
