using AxisApp.Models;
using Supabase.Postgrest;

namespace AxisApp.Services;

public class SupabaseRecurringExpensesRepository : IRecurringExpensesRepository
{
    private readonly Supabase.Client client;
    private readonly IAuthService authService;

    public SupabaseRecurringExpensesRepository(Supabase.Client client, IAuthService authService)
    {
        this.client = client;
        this.authService = authService;
    }

    public async Task<List<RecurringExpense>> GetForGroupAsync(Guid groupId)
    {
        var result = await client.From<RecurringExpense>()
            .Filter("group_id", Constants.Operator.Equals, groupId.ToString())
            .Order("start_date", Constants.Ordering.Ascending)
            .Get();

        return result.Models;
    }

    public async Task<RecurringExpense?> GetByIdAsync(Guid recurringExpenseId) =>
        await client.From<RecurringExpense>()
            .Filter("id", Constants.Operator.Equals, recurringExpenseId.ToString())
            .Single();

    public async Task<List<RecurringExpenseShare>> GetSharesAsync(Guid recurringExpenseId)
    {
        var result = await client.From<RecurringExpenseShare>()
            .Filter("recurring_expense_id", Constants.Operator.Equals, recurringExpenseId.ToString())
            .Get();

        return result.Models;
    }

    /// <summary>Same no-transaction caveat as SupabaseExpensesRepository.AddAsync — if the shares
    /// insert fails after the template succeeds, the caller ends up with a template that has no
    /// shares yet and should retry the shares insert rather than the whole thing.</summary>
    public async Task<RecurringExpense> AddAsync(RecurringExpense recurringExpense, List<RecurringExpenseShare> shares)
    {
        recurringExpense.CreatedBy = authService.RequireAccountId();
        var inserted = await client.From<RecurringExpense>().Insert(recurringExpense);
        var recurringExpenseId = inserted.Model!.Id;

        foreach (var share in shares)
            share.RecurringExpenseId = recurringExpenseId;

        await client.From<RecurringExpenseShare>().Insert(shares);

        return inserted.Model!;
    }

    /// <summary>Same reconcile-by-member_id logic as SupabaseExpensesRepository.UpdateAsync —
    /// updates share_amount for members still included, inserts newly-added participants, deletes
    /// removed ones, via an explicit recurring_expense_id+member_id Filter rather than trusting
    /// Update(model)'s implicit primary-key match (RecurringExpenseShare only marks
    /// RecurringExpenseId with [PrimaryKey], same composite-key shape as ExpenseShare).</summary>
    public async Task<RecurringExpense> UpdateAsync(RecurringExpense recurringExpense, List<RecurringExpenseShare> shares)
    {
        var updated = await client.From<RecurringExpense>().Update(recurringExpense);

        var existingShares = await GetSharesAsync(recurringExpense.Id);
        var existingMemberIds = existingShares.Select(s => s.MemberId).ToHashSet();
        var newMemberIds = shares.Select(s => s.MemberId).ToHashSet();

        foreach (var removed in existingShares.Where(s => !newMemberIds.Contains(s.MemberId)))
            await client.From<RecurringExpenseShare>()
                .Filter("recurring_expense_id", Constants.Operator.Equals, recurringExpense.Id.ToString())
                .Filter("member_id", Constants.Operator.Equals, removed.MemberId.ToString())
                .Delete();

        var toInsert = new List<RecurringExpenseShare>();
        foreach (var share in shares)
        {
            share.RecurringExpenseId = recurringExpense.Id;
            if (existingMemberIds.Contains(share.MemberId))
                await client.From<RecurringExpenseShare>()
                    .Filter("recurring_expense_id", Constants.Operator.Equals, recurringExpense.Id.ToString())
                    .Filter("member_id", Constants.Operator.Equals, share.MemberId.ToString())
                    .Update(share);
            else
                toInsert.Add(share);
        }

        if (toInsert.Count > 0)
            await client.From<RecurringExpenseShare>().Insert(toInsert);

        return updated.Model!;
    }

    /// <summary>No Postgrest fluent partial-update helper exists in this codebase (confirmed —
    /// every other repository's Update() sends a full model), so this fetches the row, flips the
    /// one field, and sends the full model back, consistent with everything else here.</summary>
    public async Task SetActiveAsync(Guid recurringExpenseId, bool isActive)
    {
        var recurringExpense = await GetByIdAsync(recurringExpenseId)
            ?? throw new InvalidOperationException("Recurring expense not found.");
        recurringExpense.IsActive = isActive;
        await client.From<RecurringExpense>().Update(recurringExpense);
    }

    public async Task DeleteAsync(Guid recurringExpenseId) =>
        await client.From<RecurringExpense>()
            .Filter("id", Constants.Operator.Equals, recurringExpenseId.ToString())
            .Delete();
}
