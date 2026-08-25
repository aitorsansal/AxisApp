using AxisApp.Models;
using Supabase.Postgrest;

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

    public async Task<Expense?> GetByIdAsync(Guid expenseId) =>
        await client.From<Expense>()
            .Filter("id", Constants.Operator.Equals, expenseId.ToString())
            .Single();

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

    /// <summary>Updates the expense row, then reconciles expense_shares against the new list:
    /// updates share_amount for members still included, inserts newly-added participants, deletes
    /// removed ones. Share updates go through an explicit expense_id+member_id Filter rather than
    /// Update(model)'s implicit primary-key match — ExpenseShare only marks ExpenseId with
    /// [PrimaryKey] (matching GroupMember's existing pattern for a composite key), so relying on
    /// that alone here would match every share row for the expense instead of just this member's.
    /// Same no-transaction caveat as AddAsync: a failure partway through leaves the expense and
    /// shares out of sync rather than rolled back together.</summary>
    public async Task<Expense> UpdateAsync(Expense expense, List<ExpenseShare> shares)
    {
        var updatedExpense = await client.From<Expense>().Update(expense);

        var existingShares = await GetSharesAsync(expense.Id);
        var existingMemberIds = existingShares.Select(s => s.MemberId).ToHashSet();
        var newMemberIds = shares.Select(s => s.MemberId).ToHashSet();

        foreach (var removed in existingShares.Where(s => !newMemberIds.Contains(s.MemberId)))
            await client.From<ExpenseShare>()
                .Filter("expense_id", Constants.Operator.Equals, expense.Id.ToString())
                .Filter("member_id", Constants.Operator.Equals, removed.MemberId.ToString())
                .Delete();

        var toInsert = new List<ExpenseShare>();
        foreach (var share in shares)
        {
            share.ExpenseId = expense.Id;
            if (existingMemberIds.Contains(share.MemberId))
                await client.From<ExpenseShare>()
                    .Filter("expense_id", Constants.Operator.Equals, expense.Id.ToString())
                    .Filter("member_id", Constants.Operator.Equals, share.MemberId.ToString())
                    .Update(share);
            else
                toInsert.Add(share);
        }

        if (toInsert.Count > 0)
            await client.From<ExpenseShare>().Insert(toInsert);

        return updatedExpense.Model!;
    }

    public async Task DeleteAsync(Guid expenseId) =>
        await client.From<Expense>()
            .Filter("id", Constants.Operator.Equals, expenseId.ToString())
            .Delete();
}
