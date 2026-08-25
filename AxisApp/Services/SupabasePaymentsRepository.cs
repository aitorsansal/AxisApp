using AxisApp.Models;
using Supabase.Postgrest;

namespace AxisApp.Services;

public class SupabasePaymentsRepository : IPaymentsRepository
{
    private readonly Supabase.Client client;
    private readonly IAuthService authService;

    public SupabasePaymentsRepository(Supabase.Client client, IAuthService authService)
    {
        this.client = client;
        this.authService = authService;
    }

    public async Task<List<Payment>> GetForGroupAsync(Guid groupId)
    {
        var result = await client.From<Payment>()
            .Filter("group_id", Constants.Operator.Equals, groupId.ToString())
            .Order("occurred_at", Constants.Ordering.Descending)
            .Get();

        return result.Models;
    }

    public async Task AddAsync(Payment payment)
    {
        payment.CreatedBy = authService.RequireAccountId();
        await client.From<Payment>().Insert(payment);
    }

    public async Task UpdateAsync(Payment payment) =>
        await client.From<Payment>().Update(payment);

    public async Task DeleteAsync(Guid paymentId) =>
        await client.From<Payment>()
            .Filter("id", Constants.Operator.Equals, paymentId.ToString())
            .Delete();
}
