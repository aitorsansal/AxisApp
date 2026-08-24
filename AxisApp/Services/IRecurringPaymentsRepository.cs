using AxisApp.Models;

namespace AxisApp.Services;

public interface IRecurringPaymentsRepository
{
    Task<List<RecurringPayment>> GetForGroupAsync(Guid groupId);
    Task AddAsync(RecurringPayment recurringPayment);
    Task UpdateAsync(RecurringPayment recurringPayment);
    Task DeleteAsync(Guid recurringPaymentId);
}
