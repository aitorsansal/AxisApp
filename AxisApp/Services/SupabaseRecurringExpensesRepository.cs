using AxisApp.Models;
using Supabase.Postgrest;

namespace AxisApp.Services;

public class SupabaseRecurringExpensesRepository : IRecurringExpensesRepository
{
    private readonly Supabase.Client client;

    public SupabaseRecurringExpensesRepository(Supabase.Client client)
    {
        this.client = client;
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

    public Task<RecurringExpense> AddAsync(RecurringExpense recurringExpense, List<RecurringExpenseShare> shares) =>
        SaveAsync(recurringExpense, shares, isNew: true);

    public Task<RecurringExpense> UpdateAsync(RecurringExpense recurringExpense, List<RecurringExpenseShare> shares) =>
        SaveAsync(recurringExpense, shares, isNew: false);

    /// <summary>Same atomic shape as SupabaseExpensesRepository.SaveAsync, through
    /// save_recurring_expense(). The function never writes created_by/created_at/
    /// last_processed_date/is_active on update — the schedule belongs to
    /// materialize_recurring_expenses() and pausing to SetActiveAsync, so editing a template's
    /// amount/split can't reset or reactivate it.</summary>
    private async Task<RecurringExpense> SaveAsync(RecurringExpense recurringExpense, List<RecurringExpenseShare> shares, bool isNew)
    {
        var response = await client.Rpc("save_recurring_expense", new Dictionary<string, object?>
        {
            ["p_template"] = new Dictionary<string, object?>
            {
                ["id"] = isNew ? null : recurringExpense.Id.ToString(),
                ["group_id"] = recurringExpense.GroupId?.ToString(),
                ["paid_by_member_id"] = recurringExpense.PaidByMemberId.ToString(),
                ["amount"] = recurringExpense.Amount,
                ["currency"] = recurringExpense.Currency,
                ["description"] = recurringExpense.Description,
                ["category"] = recurringExpense.Category,
                ["frequency"] = recurringExpense.Frequency,
                ["start_date"] = recurringExpense.StartDate.ToString("yyyy-MM-dd"),
            },
            ["p_shares"] = shares
                .Select(share => new Dictionary<string, object?>
                {
                    ["member_id"] = share.MemberId.ToString(),
                    ["share_amount"] = share.ShareAmount,
                })
                .ToList(),
        });
        var raw = response.Content?.Trim('"')
            ?? throw new InvalidOperationException("save_recurring_expense returned no id.");

        return await GetByIdAsync(Guid.Parse(raw))
            ?? throw new InvalidOperationException("save_recurring_expense succeeded but the template could not be re-fetched.");
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
