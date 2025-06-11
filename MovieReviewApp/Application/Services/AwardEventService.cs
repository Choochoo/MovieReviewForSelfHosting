using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Service responsible for managing award events and their lifecycle.
/// Handles CRUD operations and event state management.
/// </summary>
public class AwardEventService(MongoDbService databaseService, ILogger<AwardEventService> logger)
    : BaseService<AwardEvent>(databaseService, logger)
{
    // Base CRUD methods are inherited from BaseService<AwardEvent>
    // GetAllAsync, GetByIdAsync(Guid), CreateAsync, UpdateAsync, DeleteAsync(Guid)

    public async Task<List<AwardEvent>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        List<AwardEvent> events = await GetAllAsync();
        return events
            .Where(e => e.StartDate >= startDate && e.EndDate <= endDate)
            .OrderByDescending(e => e.StartDate)
            .ToList();
    }

    public async Task<AwardEvent?> GetCurrentEventAsync()
    {
        List<AwardEvent> events = await GetAllAsync();
        return events
            .Where(e => e.StartDate <= DateTime.UtcNow && e.EndDate >= DateTime.UtcNow)
            .OrderByDescending(e => e.StartDate)
            .FirstOrDefault();
    }

    public async Task<List<AwardEvent>> GetUpcomingEventsAsync()
    {
        List<AwardEvent> events = await GetAllAsync();
        return events
            .Where(e => e.StartDate > DateTime.UtcNow)
            .OrderBy(e => e.StartDate)
            .ToList();
    }

    public async Task<List<AwardEvent>> GetPastEventsAsync()
    {
        List<AwardEvent> events = await GetAllAsync();
        return events
            .Where(e => e.EndDate < DateTime.UtcNow)
            .OrderByDescending(e => e.EndDate)
            .ToList();
    }

    public async Task<AwardEvent?> GetAwardEventForDateAsync(DateTime date)
    {
        List<AwardEvent> events = await GetAllAsync();
        return events
            .Where(e => e.StartDate.Date <= date.Date && e.EndDate.Date >= date.Date)
            .FirstOrDefault();
    }
}
