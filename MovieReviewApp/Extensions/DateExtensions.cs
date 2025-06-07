using System;

namespace MovieReviewApp.Extensions
{
    public static class DateExtensions
    {
        public static DateTime StartOfDay(this DateTime dateTime)
        {
            return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, 0, 0, 0, 0);
        }

        public static DateTime EndOfDay(this DateTime dateTime)
        {
            return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, 23, 59, 59, 999);
        }

        public static bool IsWithinRange(this DateTime currentDate, DateTime startDate, DateTime endDate)
        {
            return currentDate >= startDate && currentDate <= endDate;
        }

        public static DateTime StartOfMonth(this DateTime dateTime)
        {
            return new DateTime(dateTime.Year, dateTime.Month, 1, 0, 0, 0, 0);
        }

        public static DateTime EndOfMonth(this DateTime dateTime)
        {
            return new DateTime(dateTime.Year, dateTime.Month, DateTime.DaysInMonth(dateTime.Year, dateTime.Month), 23, 59, 59, 999);
        }

        public static DateTime StartOfYear(this DateTime dateTime)
        {
            return new DateTime(dateTime.Year, 1, 1, 0, 0, 0, 0);
        }

        public static DateTime EndOfYear(this DateTime dateTime)
        {
            return new DateTime(dateTime.Year, 12, 31, 23, 59, 59, 999);
        }

        public static DateTime LastFridayOfMonth(this DateTime dateTime)
        {
            var lastDayOfMonth = new DateTime(dateTime.Year, dateTime.Month, DateTime.DaysInMonth(dateTime.Year, dateTime.Month));
            var dayOfWeek = (int)lastDayOfMonth.DayOfWeek;
            var daysToSubtract = dayOfWeek == 5 ? 0 : (dayOfWeek + 2) % 7;
            return lastDayOfMonth.AddDays(-daysToSubtract);
        }
    }
}
