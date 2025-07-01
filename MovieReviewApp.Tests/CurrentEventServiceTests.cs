using MovieReviewApp.Application.Services;
using MovieReviewApp.Models;
using MovieReviewApp.Utilities;
using Xunit;

namespace MovieReviewApp.Tests;

[Collection("Sequential")]
public class CurrentEventServiceTests
{
    [Fact]
    public async Task GetCurrentEventAsync_WithMockData_ShouldReturnCorrectEvent()
    {
        // Arrange
        DateProvider.SetCustomDate(new DateTime(2025, 7, 1, 12, 0, 0)); // July 1st, noon
        
        List<MovieEvent> testEvents = new List<MovieEvent>
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
        
        ICurrentEventService mockService = new MockCurrentEventService(testEvents);
        
        // Act
        MovieEvent? currentEvent = await mockService.GetCurrentEventAsync();
        
        // Assert
        Assert.NotNull(currentEvent);
        Assert.Equal("Jared", currentEvent.Person);
        
        // Cleanup
        DateProvider.ResetDate();
    }

    [Fact]
    public async Task GetNextEventAsync_WithMockData_ShouldReturnCorrectEvent()
    {
        // Arrange
        DateProvider.SetCustomDate(new DateTime(2025, 6, 15, 12, 0, 0)); // June 15th
        
        List<MovieEvent> testEvents = new List<MovieEvent>
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
        
        ICurrentEventService mockService = new MockCurrentEventService(testEvents);
        
        // Act
        MovieEvent? nextEvent = await mockService.GetNextEventAsync();
        
        // Assert
        Assert.NotNull(nextEvent);
        Assert.Equal("Jared", nextEvent.Person);
        
        // Cleanup
        DateProvider.ResetDate();
    }

    [Fact]
    public async Task GetMostRecentPastEventAsync_WithMockData_ShouldReturnCorrectEvent()
    {
        // Arrange
        DateProvider.SetCustomDate(new DateTime(2025, 8, 1, 12, 0, 0)); // August 1st
        
        List<MovieEvent> testEvents = new List<MovieEvent>
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
        
        ICurrentEventService mockService = new MockCurrentEventService(testEvents);
        
        // Act
        MovieEvent? pastEvent = await mockService.GetMostRecentPastEventAsync();
        
        // Assert
        Assert.NotNull(pastEvent);
        Assert.Equal("Jared", pastEvent.Person);
        
        // Cleanup
        DateProvider.ResetDate();
    }
}