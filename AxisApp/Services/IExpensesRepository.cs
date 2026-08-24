using AxisApp.Models;

namespace AxisApp.Services;

/// <summary>N-way bill splitting: an expense one member fronted, split across participants.</summary>
public interface IExpensesRepository
{
    Task<List<Expense>> GetForGroupAsync(Guid groupId);
    Task<Expense?> GetByIdAsync(Guid expenseId);
    Task<List<ExpenseShare>> GetSharesAsync(Guid expenseId);

    /// <summary>Creates an expense and its per-member shares together.</summary>
    Task<Expense> AddAsync(Expense expense, List<ExpenseShare> shares);

    /// <summary>Updates the expense and reconciles its shares against the new list — updates
    /// share amounts for members still included, inserts newly-added participants, deletes
    /// removed ones.</summary>
    Task<Expense> UpdateAsync(Expense expense, List<ExpenseShare> shares);

    Task DeleteAsync(Guid expenseId);
}
