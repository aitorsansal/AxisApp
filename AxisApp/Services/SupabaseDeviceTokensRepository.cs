using AxisApp.Models;
using Supabase.Postgrest;

namespace AxisApp.Services;

public class SupabaseDeviceTokensRepository : IDeviceTokensRepository
{
    private readonly Supabase.Client client;
    private readonly IAuthService authService;

    public SupabaseDeviceTokensRepository(Supabase.Client client, IAuthService authService)
    {
        this.client = client;
        this.authService = authService;
    }

    /// <summary>Idempotent by design: FCM hands back the same token across repeat calls (this is
    /// invoked on every Groups-page load, not just first sign-in), and push_token carries a real
    /// unique constraint — a second plain Insert with the same token would throw. Clearing any
    /// existing row for this exact token first, rather than an Upsert/OnConflict call, sidesteps
    /// needing to verify that part of the Postgrest client's API surface at all.</summary>
    public async Task RegisterAsync(string pushToken, string platform)
    {
        await client.From<DeviceToken>()
            .Filter("push_token", Constants.Operator.Equals, pushToken)
            .Delete();

        var token = new DeviceToken
        {
            AccountId = authService.RequireAccountId(),
            PushToken = pushToken,
            Platform = platform
        };

        await client.From<DeviceToken>().Insert(token);
    }

    public async Task UnregisterAsync(string pushToken) =>
        await client.From<DeviceToken>()
            .Filter("push_token", Constants.Operator.Equals, pushToken)
            .Delete();
}
