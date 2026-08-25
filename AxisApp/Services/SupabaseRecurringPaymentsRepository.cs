using AxisApp.Models;
using Supabase.Postgrest;

namespace AxisApp.Services;

public class SupabaseRecurringPaymentsRepository : IRecurringPaymentsRepository
{
    private readonly Supabase.Client client;
    private readonly IAuthService authService;

    public SupabaseRecurringPaymentsRepository(Supabase.Client client, IAuthService authService)
    {
        this.client = client;
        this.authService = authService;
    }

    public async Task<List<RecurringPayment>> GetForGroupAsync(Guid groupId)
    {
        var result = await client.From<RecurringPayment>()
            .Filter("group_id", Constants.Operator.Equals, groupId.ToString())
            .Get();

        return result.Models;
    }

    public async Task AddAsync(RecurringPayment recurringPayment)
    {
        recurringPayment.CreatedBy = authService.RequireAccountId();
        await client.From<RecurringPayment>().Insert(recurringPayment);
    }

    public async Task UpdateAsync(RecurringPayment recurringPayment) =>
        await client.From<RecurringPayment>().Update(recurringPayment);

    public async Task DeleteAsync(Guid recurringPaymentId) =>
        await client.From<RecurringPayment>()
            .Filter("id", Constants.Operator.Equals, recurringPaymentId.ToString())
            .Delete();
}
