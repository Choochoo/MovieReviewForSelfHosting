using MovieReviewApp.Infrastructure.Repositories;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Service responsible for managing award events and their lifecycle.
/// Handles CRUD operations and event state management.
/// </summary>
public class AwardEventService(IRepository<AwardEvent> repository, ILogger<AwardEventService> logger)
    : BaseService<AwardEvent>(repository, logger)
{
    // Base CRUD methods are inherited from BaseService<AwardEvent>
    // GetAllAsync, GetByIdAsync(Guid), CreateAsync, UpdateAsync, DeleteAsync(Guid)

    public async Task<AwardEvent?> GetCurrentEventAsync()
    {
        List<AwardEvent> events = await GetAllAsync();
        return events
            .Where(e => e.StartDate <= DateTime.UtcNow && e.EndDate >= DateTime.UtcNow)
            .OrderByDescending(e => e.StartDate)
            .FirstOrDefault();
    }

    public async Task<AwardEvent?> GetAwardEventForDateAsync(DateTime date)
    {
        List<AwardEvent> events = await GetAllAsync();
        return events
            .Where(e => e.StartDate.Date <= date.Date && e.EndDate.Date >= date.Date)
            .FirstOrDefault();
    }

    public async Task<AwardEvent?> GetLastCompletedAsync()
    {
        List<AwardEvent> events = await GetAllAsync();
        return events
            .Where(e => e.EndDate < DateTime.UtcNow)
            .OrderByDescending(e => e.EndDate)
            .FirstOrDefault();
    }
}
