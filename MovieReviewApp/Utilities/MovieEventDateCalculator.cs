using MovieReviewApp.Extensions;

namespace MovieReviewApp.Utilities;

public static class MovieEventDateCalculator
{
    public static (DateTime StartDate, DateTime EndDate) CalculateMonthBoundaries(DateTime month)
    {
        DateTime startDate = month.StartOfMonth();
        DateTime endDate = month.EndOfMonth();
        return (startDate, endDate);
    }

    public static DateTime GetCurrentEventMonth()
    {
        return DateProvider.Now.StartOfMonth();
    }

    public static DateTime GetNextEventMonth(DateTime currentMonth)
    {
        return currentMonth.AddMonths(1).StartOfMonth();
    }

    public static bool IsDateInMonth(DateTime date, DateTime month)
    {
        (DateTime start, DateTime end) = CalculateMonthBoundaries(month);
        return date >= start && date <= end;
    }
}