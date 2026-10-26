namespace AxisApp.Models;

public static class RecurringSchedule
{
    public static DateTime NextDue(DateTime startDate, DateTime? lastProcessedDate, string frequency)
    {
        if (lastProcessedDate is not { } basis) return startDate.Date;
        var next = frequency switch
        {
            "daily" => basis.AddDays(1),
            "weekly" => basis.AddDays(7),
            "monthly" => basis.AddMonths(1),
            "yearly" => basis.AddYears(1),
            _ => basis
        };
        return next < startDate ? startDate.Date : next.Date;
    }
}
