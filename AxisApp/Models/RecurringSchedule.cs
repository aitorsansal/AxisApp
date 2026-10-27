namespace AxisApp.Models;

public static class RecurringSchedule
{
    /// <summary>Mirrors materialize_recurring_expenses: monthly/yearly occurrences are anchored on
    /// startDate (start + N periods), so a day-31 template returns to the 31st after February.</summary>
    public static DateTime NextDue(DateTime startDate, DateTime? lastProcessedDate, string frequency)
    {
        if (lastProcessedDate is not { } basis) return startDate.Date;
        var next = frequency switch
        {
            "daily" => basis.AddDays(1),
            "weekly" => basis.AddDays(7),
            "monthly" => startDate.AddMonths((basis.Year - startDate.Year) * 12 + basis.Month - startDate.Month + 1),
            "yearly" => startDate.AddYears(basis.Year - startDate.Year + 1),
            _ => basis
        };
        return next < startDate ? startDate.Date : next.Date;
    }
}
