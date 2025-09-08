using System;

namespace MovieReviewApp.Extensions
{
    public static class DateExtensions
    {
        public static DateTime ToLocalDisplay(this DateTime dateTime)
        {
            if (dateTime.Kind == DateTimeKind.Utc) return dateTime.ToLocalTime();
            return dateTime;
        }

        public static DateTime? ToLocalDisplay(this DateTime? dateTime)
        {
            if (!dateTime.HasValue) return null;
            return dateTime.Value.ToLocalDisplay();
        }

        public static DateTimeOffset ToLocalDisplay(this DateTimeOffset dateTime)
        {
            return dateTime.ToLocalTime();
        }

        public static DateTimeOffset? ToLocalDisplay(this DateTimeOffset? dateTime)
        {
            if (!dateTime.HasValue) return null;
            return dateTime.Value.ToLocalTime();
        }

        public static DateTime StartOfDay(this DateTime dateTime)
        {
            return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, 0, 0, 0, 0, DateTimeKind.Local);
        }

        public static DateTime EndOfDay(this DateTime dateTime)
        {
            return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, 23, 59, 59, 999, DateTimeKind.Local);
        }

        public static bool IsWithinRange(this DateTime currentDate, DateTime startDate, DateTime endDate)
        {
            DateTime currentLocal = currentDate.ToLocalDisplay();
            DateTime startLocal = startDate.ToLocalDisplay();
            DateTime endLocal = endDate.ToLocalDisplay();
            return currentLocal >= startLocal && currentLocal <= endLocal;
        }

        public static DateTime StartOfMonth(this DateTime dateTime)
        {
            return new DateTime(dateTime.Year, dateTime.Month, 1, 0, 0, 0, 0, DateTimeKind.Local);
        }

        public static DateTime EndOfMonth(this DateTime dateTime)
        {
            return new DateTime(dateTime.Year, dateTime.Month, DateTime.DaysInMonth(dateTime.Year, dateTime.Month), 23, 59, 59, 999, DateTimeKind.Local);
        }

        public static DateTime StartOfYear(this DateTime dateTime)
        {
            return new DateTime(dateTime.Year, 1, 1, 0, 0, 0, 0, DateTimeKind.Local);
        }

        public static DateTime EndOfYear(this DateTime dateTime)
        {
            return new DateTime(dateTime.Year, 12, 31, 23, 59, 59, 999, DateTimeKind.Local);
        }

        public static DateTime LastFridayOfMonth(this DateTime dateTime)
        {
            DateTime lastDayOfMonth = new DateTime(dateTime.Year, dateTime.Month, DateTime.DaysInMonth(dateTime.Year, dateTime.Month), 0, 0, 0, 0, DateTimeKind.Local);
            int dayOfWeek = (int)lastDayOfMonth.DayOfWeek;
            int daysToSubtract = dayOfWeek == 5 ? 0 : (dayOfWeek + 2) % 7;
            return lastDayOfMonth.AddDays(-daysToSubtract);
        }
    }
}
