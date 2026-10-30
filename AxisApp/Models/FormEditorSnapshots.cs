using System.Globalization;
using System.Text.Json;

namespace AxisApp.Models;

public sealed record EventEditorState(string Title, string Description, string Location,
    DateTime StartDate, TimeSpan StartTime, bool HasEndTime, DateTime EndDate, TimeSpan EndTime, bool NeedsTransport);

public sealed record SettlementEditorState(string Amount, string Note, DateTime Date,
    Guid? Payer, Guid? Recipient, string Currency);

public static class FormEditorSnapshots
{
    public static string Event(EventEditorState state) => JsonSerializer.Serialize(new
    {
        state.Title, state.Description, state.Location,
        StartDate = state.StartDate.Date, state.StartTime, state.HasEndTime,
        EndDate = state.HasEndTime ? state.EndDate.Date : (DateTime?)null,
        EndTime = state.HasEndTime ? state.EndTime : (TimeSpan?)null, state.NeedsTransport
    });

    public static string Settlement(SettlementEditorState state) => JsonSerializer.Serialize(new
    {
        Amount = AmountInput.TryParse(state.Amount, state.Currency, out var amount)
            ? amount.ToString("0.##", CultureInfo.InvariantCulture) : "invalid:" + state.Amount,
        state.Note, Date = state.Date.Date, state.Payer, state.Recipient, state.Currency
    });
}
