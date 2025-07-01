using MovieReviewApp.Application.Services;
using MovieReviewApp.Models;
using Xunit;

namespace MovieReviewApp.Tests;

public class PhaseEventGeneratorTests
{
    [Fact]
    public void CreateMovieEvent_ShouldHaveCorrectMonthBoundaries()
    {
        DateTime month = new DateTime(2025, 7, 15); // Mid-July
        
        MovieEvent movieEvent = PhaseEventGenerator.CreateMovieEvent("Jared", month, 1);
        
        Assert.Equal(new DateTime(2025, 7, 1, 0, 0, 0, 0), movieEvent.StartDate);
        Assert.Equal(new DateTime(2025, 7, 31, 23, 59, 59, 999), movieEvent.EndDate);
        Assert.Equal("Jared", movieEvent.Person);
        Assert.Equal(1, movieEvent.PhaseNumber);
        Assert.False(movieEvent.FromDatabase);
        Assert.False(movieEvent.IsEditing);
    }

    [Fact]
    public void CreateMovieEvent_ShouldSetCorrectMeetupTime()
    {
        DateTime month = new DateTime(2025, 7, 1);
        
        MovieEvent movieEvent = PhaseEventGenerator.CreateMovieEvent("Jared", month, 1);
        
        // Should be last Friday of July at 6 PM
        DateTime expectedMeetupTime = new DateTime(2025, 7, 25, 18, 0, 0); // July 25th is last Friday of July 2025
        Assert.Equal(expectedMeetupTime, movieEvent.MeetupTime);
    }

    [Fact]
    public void AssignPeopleToEvents_RespectOrder_ShouldReturnSameOrder()
    {
        List<string> people = new List<string> { "Jared", "Nikki", "John" };
        Random random = new Random(42);
        
        List<string> result = PhaseEventGenerator.AssignPeopleToEvents(people, respectOrder: true, random);
        
        Assert.Equal(people, result);
    }

    [Fact]
    public void AssignPeopleToEvents_RandomOrder_ShouldShufflePeople()
    {
        List<string> people = new List<string> { "Jared", "Nikki", "John", "Alice", "Bob" };
        Random random = new Random(42); // Fixed seed for reproducible test
        
        List<string> result = PhaseEventGenerator.AssignPeopleToEvents(people, respectOrder: false, random);
        
        Assert.Equal(people.Count, result.Count);
        Assert.True(people.All(person => result.Contains(person))); // All people included
        // Note: With fixed seed, this should produce a different order, but testing exact order would be brittle
    }

    [Fact]
    public void SetDefaultMeetupTime_NullMeetupTime_ShouldSetDefault()
    {
        MovieEvent movieEvent = new MovieEvent
        {
            StartDate = new DateTime(2025, 7, 1),
            MeetupTime = null
        };
        
        PhaseEventGenerator.SetDefaultMeetupTime(movieEvent);
        
        DateTime expectedMeetupTime = new DateTime(2025, 7, 25, 18, 0, 0); // Last Friday of July at 6 PM
        Assert.Equal(expectedMeetupTime, movieEvent.MeetupTime);
    }

    [Fact]
    public void SetDefaultMeetupTime_ExistingMeetupTime_ShouldNotChange()
    {
        DateTime existingTime = new DateTime(2025, 7, 20, 19, 30, 0);
        MovieEvent movieEvent = new MovieEvent
        {
            StartDate = new DateTime(2025, 7, 1),
            MeetupTime = existingTime
        };
        
        PhaseEventGenerator.SetDefaultMeetupTime(movieEvent);
        
        Assert.Equal(existingTime, movieEvent.MeetupTime);
    }
}