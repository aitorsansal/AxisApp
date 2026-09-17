using AxisApp.Models;

namespace AxisApp.Services;

/// <summary>Templates for periodically auto-generated N-way split expenses. Mirrors
/// IExpensesRepository's shape — see RecurringExpense's remarks for the template/instance
/// relationship.</summary>
public interface IRecurringExpensesRepository
{
    Task<List<RecurringExpense>> GetForGroupAsync(Guid groupId);
    Task<RecurringExpense?> GetByIdAsync(Guid recurringExpenseId);
    Task<List<RecurringExpenseShare>> GetSharesAsync(Guid recurringExpenseId);

    /// <summary>Creates a template and its per-member shares in one transaction.</summary>
    Task<RecurringExpense> AddAsync(RecurringExpense recurringExpense, List<RecurringExpenseShare> shares);

    /// <summary>Updates the template and reconciles its shares against the new list — same
    /// shape as IExpensesRepository.UpdateAsync. Never writes CreatedBy/CreatedAt/
    /// LastProcessedDate/IsActive (server-side, see save_recurring_expense()), so editing a
    /// template's amount/split/category can't reset its schedule or reactivate it.</summary>
    Task<RecurringExpense> UpdateAsync(RecurringExpense recurringExpense, List<RecurringExpenseShare> shares);

    /// <summary>Pauses/resumes a template without touching anything else about it.</summary>
    Task SetActiveAsync(Guid recurringExpenseId, bool isActive);

    Task DeleteAsync(Guid recurringExpenseId);
}
