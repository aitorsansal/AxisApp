using AxisApp.Models;

namespace AxisApp.Services;

public interface IPaymentsRepository
{
    Task<List<Payment>> GetForGroupAsync(Guid groupId);
    Task AddAsync(Payment payment);
    Task UpdateAsync(Payment payment);
    Task DeleteAsync(Guid paymentId);
}
