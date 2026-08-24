using AxisApp.Models;

namespace AxisApp.Services;

/// <summary>N-way bill splitting: an expense one member fronted, split across participants.</summary>
public interface IExpensesRepository
{
    Task<List<Expense>> GetForGroupAsync(Guid groupId);
    Task<List<ExpenseShare>> GetSharesAsync(Guid expenseId);

    /// <summary>Creates an expense and its per-member shares together.</summary>
    Task<Expense> AddAsync(Expense expense, List<ExpenseShare> shares);
    Task DeleteAsync(Guid expenseId);
}
