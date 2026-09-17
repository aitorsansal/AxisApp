using AxisApp.Models;
using Supabase.Postgrest;

namespace AxisApp.Services;

public class SupabaseExpensesRepository : IExpensesRepository
{
    private readonly Supabase.Client client;
    private readonly IWidgetRefreshService widgetRefresh;

    public SupabaseExpensesRepository(Supabase.Client client, IWidgetRefreshService widgetRefresh)
    {
        this.client = client;
        this.widgetRefresh = widgetRefresh;
    }

    public async Task<List<Expense>> GetForGroupAsync(Guid groupId, int limit, int offset)
    {
        var result = await client.From<Expense>()
            .Filter("group_id", Constants.Operator.Equals, groupId.ToString())
            .Order("occurred_at", Constants.Ordering.Descending)
            .Order("created_at", Constants.Ordering.Descending)
            .Range(offset, offset + limit - 1)
            .Get();

        return result.Models;
    }

    public async Task<List<Expense>> GetAllForGroupAsync(Guid groupId)
    {
        var result = await client.From<Expense>()
            .Filter("group_id", Constants.Operator.Equals, groupId.ToString())
            .Get();

        return result.Models;
    }

    public async Task<List<Expense>> GetForEventAsync(Guid eventId)
    {
        var result = await client.From<Expense>()
            .Filter("event_id", Constants.Operator.Equals, eventId.ToString())
            .Order("occurred_at", Constants.Ordering.Descending)
            .Get();

        return result.Models;
    }

    public async Task<List<Expense>> SearchForGroupAsync(Guid groupId, string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var result = await client.From<Expense>()
            .Filter("group_id", Constants.Operator.Equals, groupId.ToString())
            .Filter("description", Constants.Operator.ILike, $"%{query.Trim()}%")
            .Order("occurred_at", Constants.Ordering.Descending)
            .Order("created_at", Constants.Ordering.Descending)
            .Limit(limit)
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

    public async Task<List<ExpenseShare>> GetSharesForExpensesAsync(IReadOnlyCollection<Guid> expenseIds)
    {
        if (expenseIds.Count == 0) return [];

        var result = await client.From<ExpenseShare>()
            .Filter("expense_id", Constants.Operator.In, expenseIds.Select(id => id.ToString()).ToList())
            .Get();

        return result.Models;
    }

    public Task<Expense> AddAsync(Expense expense, List<ExpenseShare> shares) => SaveAsync(expense, shares, isNew: true);

    public Task<Expense> UpdateAsync(Expense expense, List<ExpenseShare> shares) => SaveAsync(expense, shares, isNew: false);

    /// <summary>Writes the expense and its full share list through save_expense() — one Postgres
    /// transaction, so a failure partway through rolls both back instead of leaving an expense
    /// with missing shares (PostgREST has no client-side transaction API; see
    /// supabase/atomic_expense_save.sql). The function also owns created_by/created_at: it sets
    /// them on insert and never touches them on update, so the "fresh object blanks server-set
    /// fields" footgun can't happen through this path. It reconciles shares by member_id itself
    /// and rejects a split that doesn't sum to Amount. Same scalar-uuid Rpc response shape as
    /// SupabaseGroupsRepository.CreateAsync, followed by a typed re-fetch.</summary>
    private async Task<Expense> SaveAsync(Expense expense, List<ExpenseShare> shares, bool isNew)
    {
        var response = await client.Rpc("save_expense", new Dictionary<string, object?>
        {
            ["p_expense"] = new Dictionary<string, object?>
            {
                ["id"] = isNew ? null : expense.Id.ToString(),
                ["group_id"] = expense.GroupId?.ToString(),
                ["paid_by_member_id"] = expense.PaidByMemberId.ToString(),
                ["amount"] = expense.Amount,
                ["currency"] = expense.Currency,
                ["description"] = expense.Description,
                ["category"] = expense.Category,
                ["occurred_at"] = expense.OccurredAt,
                ["receipt_path"] = expense.ReceiptPath,
                ["is_settlement"] = expense.IsSettlement,
                ["event_id"] = expense.EventId?.ToString(),
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
            ?? throw new InvalidOperationException("save_expense returned no expense id.");
        var expenseId = Guid.Parse(raw);

        widgetRefresh.RequestBalancesRefresh();

        return await GetByIdAsync(expenseId)
            ?? throw new InvalidOperationException("save_expense succeeded but the expense could not be re-fetched.");
    }

    public async Task DeleteAsync(Guid expenseId)
    {
        await client.From<Expense>()
            .Filter("id", Constants.Operator.Equals, expenseId.ToString())
            .Delete();

        widgetRefresh.RequestBalancesRefresh();
    }
}
