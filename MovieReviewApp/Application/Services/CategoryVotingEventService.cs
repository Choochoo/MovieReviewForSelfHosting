using MovieReviewApp.Extensions;
using MovieReviewApp.Infrastructure.Repositories;
using MovieReviewApp.Models;
using MovieReviewApp.Utilities;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Service responsible for managing category voting events.
/// Handles CRUD operations for the pre-awards category voting process.
/// </summary>
public class CategoryVotingEventService(
    IRepository<CategoryVotingEvent> repository,
    ILogger<CategoryVotingEventService> logger)
    : BaseService<CategoryVotingEvent>(repository, logger)
{
    /// <summary>
    /// Gets the current category voting event (if we're in a pre-awards month)
    /// </summary>
    public async Task<CategoryVotingEvent?> GetCurrentEventAsync()
    {
        DateTime now = DateProvider.Now;
        List<CategoryVotingEvent> events = await GetAllAsync();
        return events
            .Where(e => e.Month.Year == now.Year && e.Month.Month == now.Month)
            .FirstOrDefault();
    }

    /// <summary>
    /// Gets a category voting event for a specific month
    /// </summary>
    public async Task<CategoryVotingEvent?> GetEventForMonthAsync(DateTime month)
    {
        DateTime monthStart = month.StartOfMonth();
        List<CategoryVotingEvent> events = await GetAllAsync();
        return events
            .Where(e => e.Month.Year == monthStart.Year && e.Month.Month == monthStart.Month)
            .FirstOrDefault();
    }

    /// <summary>
    /// Gets or creates a category voting event for a specific month
    /// </summary>
    public async Task<CategoryVotingEvent> GetOrCreateForMonthAsync(DateTime month, int targetPhaseNumber)
    {
        CategoryVotingEvent? existing = await GetEventForMonthAsync(month);
        if (existing != null)
            return existing;

        DateTime monthStart = month.StartOfMonth();
        DateTime monthEnd = monthStart.AddMonths(1).AddDays(-1);

        CategoryVotingEvent newEvent = new CategoryVotingEvent
        {
            Month = monthStart,
            VotingStartDate = monthStart,
            VotingEndDate = monthEnd,
            TargetAwardEventPhaseNumber = targetPhaseNumber,
            GeneratedCategories = new List<string>(),
            FinalCategories = new List<string>(),
            IsFinalized = false
        };

        return await CreateAsync(newEvent);
    }

    /// <summary>
    /// Finalizes a voting event by setting the final categories
    /// </summary>
    public async Task<CategoryVotingEvent> FinalizeAsync(Guid eventId, List<string> finalCategories)
    {
        CategoryVotingEvent? evt = await GetByIdAsync(eventId);
        if (evt == null)
            throw new InvalidOperationException($"Category voting event {eventId} not found");

        evt.FinalCategories = finalCategories;
        evt.IsFinalized = true;

        return await UpdateAsync(evt);
    }
}
