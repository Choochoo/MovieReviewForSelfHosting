using MovieReviewApp.Models;
using MovieReviewApp.Utilities;
using Xunit;

namespace MovieReviewApp.Tests;

public class EventSequenceValidatorTests
{
    [Fact]
    public void ValidateEventDates_ValidEvent_ShouldReturnTrue()
    {
        MovieEvent movieEvent = new MovieEvent
        {
            StartDate = new DateTime(2025, 7, 1, 0, 0, 0, 0),
            EndDate = new DateTime(2025, 7, 31, 23, 59, 59, 999)
        };
        
        bool result = EventSequenceValidator.ValidateEventDates(movieEvent);
        
        Assert.True(result);
    }

    [Fact]
    public void ValidateEventDates_InvalidStartDate_ShouldReturnFalse()
    {
        MovieEvent movieEvent = new MovieEvent
        {
            StartDate = new DateTime(2025, 7, 2, 0, 0, 0, 0), // Should be July 1st
            EndDate = new DateTime(2025, 7, 31, 23, 59, 59, 999)
        };
        
        bool result = EventSequenceValidator.ValidateEventDates(movieEvent);
        
        Assert.False(result);
    }

    [Fact]
    public void ValidateNoGapsBetweenEvents_ConsecutiveEvents_ShouldReturnTrue()
    {
        List<MovieEvent> events = new List<MovieEvent>
        {
            new MovieEvent 
            { 
                StartDate = new DateTime(2025, 6, 1, 0, 0, 0, 0),
                EndDate = new DateTime(2025, 6, 30, 23, 59, 59, 999)
            },
            new MovieEvent 
            { 
                StartDate = new DateTime(2025, 7, 1, 0, 0, 0, 0),
                EndDate = new DateTime(2025, 7, 31, 23, 59, 59, 999)
            }
        };
        
        bool result = EventSequenceValidator.ValidateNoGapsBetweenEvents(events);
        
        Assert.True(result);
    }

    [Fact]
    public void ValidateNoGapsBetweenEvents_GapExists_ShouldReturnFalse()
    {
        List<MovieEvent> events = new List<MovieEvent>
        {
            new MovieEvent 
            { 
                StartDate = new DateTime(2025, 6, 1, 0, 0, 0, 0),
                EndDate = new DateTime(2025, 6, 30, 23, 59, 59, 999)
            },
            new MovieEvent 
            { 
                StartDate = new DateTime(2025, 7, 3, 0, 0, 0, 0), // Gap: July 2nd missing
                EndDate = new DateTime(2025, 7, 31, 23, 59, 59, 999)
            }
        };
        
        bool result = EventSequenceValidator.ValidateNoGapsBetweenEvents(events);
        
        Assert.False(result);
    }

    [Fact]
    public void ValidateNoOverlapsBetweenEvents_NoOverlaps_ShouldReturnTrue()
    {
        List<MovieEvent> events = new List<MovieEvent>
        {
            new MovieEvent 
            { 
                StartDate = new DateTime(2025, 6, 1),
                EndDate = new DateTime(2025, 6, 30, 23, 59, 59, 999)
            },
            new MovieEvent 
            { 
                StartDate = new DateTime(2025, 7, 1),
                EndDate = new DateTime(2025, 7, 31, 23, 59, 59, 999)
            }
        };
        
        bool result = EventSequenceValidator.ValidateNoOverlapsBetweenEvents(events);
        
        Assert.True(result);
    }
}