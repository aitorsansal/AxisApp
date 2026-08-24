using AxisApp.Models;
using Postgrest;

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

    public async Task RegisterAsync(string pushToken, string platform)
    {
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
