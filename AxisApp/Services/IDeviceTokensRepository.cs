namespace AxisApp.Services;

/// <summary>Push tokens (e.g. OneSignal player ids) registered for the current account's devices.</summary>
public interface IDeviceTokensRepository
{
    Task RegisterAsync(string pushToken, string platform);
    Task UnregisterAsync(string pushToken);
}
