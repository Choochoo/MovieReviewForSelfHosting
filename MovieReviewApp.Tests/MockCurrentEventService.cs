using MovieReviewApp.Application.Services;
using MovieReviewApp.Models;
using MovieReviewApp.Utilities;
using MovieReviewApp.Extensions;

namespace MovieReviewApp.Tests;

public class MockCurrentEventService : ICurrentEventService
{
    private readonly List<MovieEvent> _events;

    public MockCurrentEventService(List<MovieEvent> events)
    {
        _events = events;
    }

    public Task<MovieEvent?> GetCurrentEventAsync()
    {
        DateTime now = DateProvider.Now;
        
        MovieEvent? currentEvent = _events
            .Where(e => now.IsWithinRange(e.StartDate, e.EndDate))
            .OrderByDescending(e => e.StartDate)
            .FirstOrDefault();
            
        return Task.FromResult(currentEvent);
    }

    public Task<MovieEvent?> GetNextEventAsync()
    {
        DateTime now = DateProvider.Now;
        
        MovieEvent? nextEvent = _events
            .Where(e => e.StartDate > now)
            .OrderBy(e => e.StartDate)
            .FirstOrDefault();
            
        return Task.FromResult(nextEvent);
    }

    public Task<MovieEvent?> GetMostRecentPastEventAsync()
    {
        DateTime now = DateProvider.Now;
        
        MovieEvent? pastEvent = _events
            .Where(e => e.EndDate < now)
            .OrderByDescending(e => e.StartDate)
            .FirstOrDefault();
            
        return Task.FromResult(pastEvent);
    }
}