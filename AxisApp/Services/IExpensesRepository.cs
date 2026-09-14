using AxisApp.Models;

namespace AxisApp.Services;

/// <summary>N-way bill splitting: an expense one member fronted, split across participants.</summary>
public interface IExpensesRepository
{
    /// <summary>Newest-first page of a group's expenses — offset pagination via limit/offset
    /// (not a keyset cursor: simpler, and good enough at this app's scale/concurrency).</summary>
    Task<List<Expense>> GetForGroupAsync(Guid groupId, int limit, int offset);

    Task<List<Expense>> GetForEventAsync(Guid eventId);

    /// <summary>Server-side description search within a group, newest-first, capped at limit —
    /// used by the Recent Activity search box instead of filtering an unbounded in-memory list.</summary>
    Task<List<Expense>> SearchForGroupAsync(Guid groupId, string query, int limit);

    Task<Expense?> GetByIdAsync(Guid expenseId);
    Task<List<ExpenseShare>> GetSharesAsync(Guid expenseId);

    /// <summary>Batched shares fetch for many expenses at once (expense_id IN (...)) — used
    /// wherever a list of expenses needs share counts/payees, to avoid an N+1 GetSharesAsync
    /// call per expense.</summary>
    Task<List<ExpenseShare>> GetSharesForExpensesAsync(IReadOnlyCollection<Guid> expenseIds);

    /// <summary>Creates an expense and its per-member shares together.</summary>
    Task<Expense> AddAsync(Expense expense, List<ExpenseShare> shares);

    /// <summary>Updates the expense and reconciles its shares against the new list — updates
    /// share amounts for members still included, inserts newly-added participants, deletes
    /// removed ones.</summary>
    Task<Expense> UpdateAsync(Expense expense, List<ExpenseShare> shares);

    Task DeleteAsync(Guid expenseId);
}
