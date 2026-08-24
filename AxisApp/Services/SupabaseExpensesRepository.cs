using AxisApp.Models;
using Postgrest;

namespace AxisApp.Services;

public class SupabaseExpensesRepository : IExpensesRepository
{
    private readonly Supabase.Client client;
    private readonly IAuthService authService;

    public SupabaseExpensesRepository(Supabase.Client client, IAuthService authService)
    {
        this.client = client;
        this.authService = authService;
    }

    public async Task<List<Expense>> GetForGroupAsync(Guid groupId)
    {
        var result = await client.From<Expense>()
            .Filter("group_id", Constants.Operator.Equals, groupId.ToString())
            .Order("occurred_at", Constants.Ordering.Descending)
            .Get();

        return result.Models;
    }

    public async Task<List<ExpenseShare>> GetSharesAsync(Guid expenseId)
    {
        var result = await client.From<ExpenseShare>()
            .Filter("expense_id", Constants.Operator.Equals, expenseId.ToString())
            .Get();

        return result.Models;
    }

    /// <summary>Inserts the expense, then its shares once the expense id exists to point them at.
    /// Not wrapped in a database transaction (Postgrest has no client-side transaction API) — if
    /// the shares insert fails after the expense succeeds, the caller ends up with an expense
    /// that has no shares yet and should retry the shares insert rather than the whole thing.</summary>
    public async Task<Expense> AddAsync(Expense expense, List<ExpenseShare> shares)
    {
        expense.CreatedBy = authService.RequireAccountId();
        var insertedExpense = await client.From<Expense>().Insert(expense);
        var expenseId = insertedExpense.Model!.Id;

        foreach (var share in shares)
            share.ExpenseId = expenseId;

        await client.From<ExpenseShare>().Insert(shares);

        return insertedExpense.Model!;
    }

    public async Task DeleteAsync(Guid expenseId) =>
        await client.From<Expense>()
            .Filter("id", Constants.Operator.Equals, expenseId.ToString())
            .Delete();
}
