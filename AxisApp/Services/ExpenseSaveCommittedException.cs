namespace AxisApp.Services;

public sealed class ExpenseSaveCommittedException(Guid expenseId, Exception innerException)
    : Exception("The expense was saved but could not be refreshed.", innerException)
{
    public Guid ExpenseId { get; } = expenseId;
}
