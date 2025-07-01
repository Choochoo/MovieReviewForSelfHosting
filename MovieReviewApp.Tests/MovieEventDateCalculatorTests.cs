using MovieReviewApp.Utilities;
using Xunit;

namespace MovieReviewApp.Tests;

public class MovieEventDateCalculatorTests
{
    [Fact]
    public void CalculateMonthBoundaries_ShouldReturnFirstToLastOfMonth()
    {
        DateTime testDate = new DateTime(2025, 7, 15);
        
        (DateTime start, DateTime end) = MovieEventDateCalculator.CalculateMonthBoundaries(testDate);
        
        Assert.Equal(new DateTime(2025, 7, 1, 0, 0, 0, 0), start);
        Assert.Equal(new DateTime(2025, 7, 31, 23, 59, 59, 999), end);
    }

    [Fact]
    public void CalculateMonthBoundaries_FebruaryLeapYear_ShouldHandle29Days()
    {
        DateTime testDate = new DateTime(2024, 2, 15); // 2024 is leap year
        
        (DateTime start, DateTime end) = MovieEventDateCalculator.CalculateMonthBoundaries(testDate);
        
        Assert.Equal(new DateTime(2024, 2, 1, 0, 0, 0, 0), start);
        Assert.Equal(new DateTime(2024, 2, 29, 23, 59, 59, 999), end);
    }

    [Fact]
    public void CalculateMonthBoundaries_FebruaryNonLeapYear_ShouldHandle28Days()
    {
        DateTime testDate = new DateTime(2025, 2, 15); // 2025 is not leap year
        
        (DateTime start, DateTime end) = MovieEventDateCalculator.CalculateMonthBoundaries(testDate);
        
        Assert.Equal(new DateTime(2025, 2, 1, 0, 0, 0, 0), start);
        Assert.Equal(new DateTime(2025, 2, 28, 23, 59, 59, 999), end);
    }

    [Fact]
    public void GetNextEventMonth_ShouldReturnFirstOfNextMonth()
    {
        DateTime currentMonth = new DateTime(2025, 7, 1);
        
        DateTime nextMonth = MovieEventDateCalculator.GetNextEventMonth(currentMonth);
        
        Assert.Equal(new DateTime(2025, 8, 1, 0, 0, 0, 0), nextMonth);
    }

    [Fact]
    public void IsDateInMonth_DateInMonth_ShouldReturnTrue()
    {
        DateTime date = new DateTime(2025, 7, 15, 14, 30, 0);
        DateTime month = new DateTime(2025, 7, 1);
        
        bool result = MovieEventDateCalculator.IsDateInMonth(date, month);
        
        Assert.True(result);
    }

    [Fact]
    public void IsDateInMonth_DateOutsideMonth_ShouldReturnFalse()
    {
        DateTime date = new DateTime(2025, 8, 1, 0, 0, 0);
        DateTime month = new DateTime(2025, 7, 1);
        
        bool result = MovieEventDateCalculator.IsDateInMonth(date, month);
        
        Assert.False(result);
    }
}