using MovieReviewApp.Models;
using MovieReviewApp.Utilities;
using MovieReviewApp.Extensions;
using Xunit;

namespace MovieReviewApp.Tests;

[Collection("Sequential")]
public class CurrentEventDetectionTests
{
    [Fact]
    public void CurrentEventDetection_July1st_ShouldFindJulyEvent()
    {
        // Arrange
        DateProvider.SetCustomDate(new DateTime(2025, 7, 1, 12, 0, 0)); // July 1st, noon
        
        List<MovieEvent> events = new List<MovieEvent>
        {
            new MovieEvent 
            { 
                Person = "Nikki",
                StartDate = new DateTime(2025, 6, 1, 0, 0, 0, 0),
                EndDate = new DateTime(2025, 6, 30, 23, 59, 59, 999)
            },
            new MovieEvent 
            { 
                Person = "Jared",
                StartDate = new DateTime(2025, 7, 1, 0, 0, 0, 0),
                EndDate = new DateTime(2025, 7, 31, 23, 59, 59, 999)
            }
        };
        
        // Act
        MovieEvent? currentEvent = events
            .FirstOrDefault(e => DateProvider.Now.IsWithinRange(e.StartDate, e.EndDate));
        
        // Assert
        Assert.NotNull(currentEvent);
        Assert.Equal("Jared", currentEvent.Person);
        
        // Cleanup
        DateProvider.ResetDate();
    }

    [Fact]
    public void CurrentEventDetection_June30th_ShouldFindJuneEvent()
    {
        // Arrange
        DateProvider.SetCustomDate(new DateTime(2025, 6, 30, 23, 30, 0)); // June 30th, 11:30 PM
        
        List<MovieEvent> events = new List<MovieEvent>
        {
            new MovieEvent 
            { 
                Person = "Nikki",
                StartDate = new DateTime(2025, 6, 1, 0, 0, 0, 0),
                EndDate = new DateTime(2025, 6, 30, 23, 59, 59, 999)
            },
            new MovieEvent 
            { 
                Person = "Jared",
                StartDate = new DateTime(2025, 7, 1, 0, 0, 0, 0),
                EndDate = new DateTime(2025, 7, 31, 23, 59, 59, 999)
            }
        };
        
        // Act
        MovieEvent? currentEvent = events
            .FirstOrDefault(e => DateProvider.Now.IsWithinRange(e.StartDate, e.EndDate));
        
        // Assert
        Assert.NotNull(currentEvent);
        Assert.Equal("Nikki", currentEvent.Person);
        
        // Cleanup
        DateProvider.ResetDate();
    }

    [Fact]
    public void NextEventDetection_June30th_ShouldFindJulyEvent()
    {
        // Arrange
        DateProvider.SetCustomDate(new DateTime(2025, 6, 30, 23, 30, 0)); // June 30th, 11:30 PM
        
        List<MovieEvent> events = new List<MovieEvent>
        {
            new MovieEvent 
            { 
                Person = "Nikki",
                StartDate = new DateTime(2025, 6, 1, 0, 0, 0, 0),
                EndDate = new DateTime(2025, 6, 30, 23, 59, 59, 999)
            },
            new MovieEvent 
            { 
                Person = "Jared",
                StartDate = new DateTime(2025, 7, 1, 0, 0, 0, 0),
                EndDate = new DateTime(2025, 7, 31, 23, 59, 59, 999)
            }
        };
        
        // Act
        MovieEvent? nextEvent = events
            .Where(e => e.StartDate > DateProvider.Now)
            .OrderBy(e => e.StartDate)
            .FirstOrDefault();
        
        // Assert
        Assert.NotNull(nextEvent);
        Assert.Equal("Jared", nextEvent.Person);
        
        // Cleanup
        DateProvider.ResetDate();
    }

    [Fact]
    public void CurrentEventDetection_BetweenMonths_ShouldReturnNull()
    {
        // This tests the exact issue from the bug report - gap between months
        DateProvider.SetCustomDate(new DateTime(2025, 7, 1, 12, 0, 0)); // July 1st, noon
        
        List<MovieEvent> events = new List<MovieEvent>
        {
            new MovieEvent 
            { 
                Person = "Nikki",
                StartDate = new DateTime(2025, 6, 1, 0, 0, 0), // June 1st start
                EndDate = new DateTime(2025, 6, 30, 23, 59, 59, 999)   // June 30th end
            },
            new MovieEvent 
            { 
                Person = "Jared",
                StartDate = new DateTime(2025, 7, 2, 0, 0, 0), // July 2nd start (gap on July 1st)
                EndDate = new DateTime(2025, 7, 31, 23, 59, 59, 999)   // July 31st end
            }
        };
        
        // Act
        MovieEvent? currentEvent = events
            .FirstOrDefault(e => DateProvider.Now.IsWithinRange(e.StartDate, e.EndDate));
        
        // Assert - This should be null due to the gap on July 1st
        Assert.Null(currentEvent);
        
        // Cleanup
        DateProvider.ResetDate();
    }
}