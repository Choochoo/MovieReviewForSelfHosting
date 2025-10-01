using MovieReviewApp.Infrastructure.Repositories;
using MovieReviewApp.Infrastructure.Services;
using MovieReviewApp.Models;
using MovieReviewApp.Extensions;
using MovieReviewApp.Utilities;
using Microsoft.Extensions.Logging;

namespace MovieReviewApp.Application.Services;

public class MovieEventService(
    IRepository<MovieEvent> repository,
    ILogger<MovieEventService> logger,
    PersonAssignmentCacheService personAssignmentCache,
    SettingService settingService,
    PersonService personService,
    DemoProtectionService demoProtection)
    : BaseService<MovieEvent>(repository, logger)
{
    private readonly PersonAssignmentCacheService _personAssignmentCache = personAssignmentCache;
    private readonly SettingService _settingService = settingService;
    private readonly PersonService _personService = personService;
    private readonly DemoProtectionService _demoProtection = demoProtection;

    /// <summary>
    /// Gets an existing movie event for the specified month, or creates a placeholder if it doesn't exist.
    /// Returns null for awards months (person assignment starts with "Awards Event").
    /// Uses PhaseCalculator for phase number calculation (no Phase database dependency).
    /// </summary>
    public async Task<MovieEvent?> GetOrCreateForMonthAsync(DateTime targetMonth)
    {
        // Check if event already exists in database
        DateTime monthStart = targetMonth.StartOfMonth();
        DateTime monthEnd = targetMonth.EndOfMonth();
        List<MovieEvent> existing = await GetByDateRangeAsync(monthStart, monthEnd);

        if (existing.Any())
            return existing.First();

        // Get person assignment from cache
        string? assignedPerson = await _personAssignmentCache.GetPersonForMonthAsync(targetMonth);

        // Skip awards months
        if (string.IsNullOrEmpty(assignedPerson) || assignedPerson.StartsWith("Awards Event"))
            return null;

        // Get settings for phase calculation
        List<Setting> settings = await _settingService.GetAllAsync();
        Setting? startDateSetting = settings.FirstOrDefault(s => s.Key == "StartDate");

        if (startDateSetting == null || !DateTime.TryParse(startDateSetting.Value, out DateTime clubStartDate))
        {
            clubStartDate = new DateTime(2024, 3, 1); // Fallback
        }

        List<Person> people = await _personService.GetAllAsync();
        int peoplePerPhase = people.Count > 0 ? people.Count : 6;

        AwardSetting? awardSettings = await _settingService.GetAwardSettingsAsync();

        // Calculate phase number using PhaseCalculator (no database dependency)
        int phaseNumber = PhaseCalculator.CalculatePhaseNumber(
            targetMonth,
            clubStartDate,
            peoplePerPhase,
            awardSettings);

        // Create and save event (no Phase record creation needed)
        MovieEvent newEvent = PhaseEventGenerator.CreateMovieEvent(assignedPerson, targetMonth, phaseNumber);
        await CreateAsync(newEvent);

        _logger.LogInformation("Created placeholder event for {Month:yyyy-MM} → {Person} (Phase {PhaseNumber})",
            targetMonth, assignedPerson, phaseNumber);

        return newEvent;
    }

    /// <summary>
    /// Gets movie titles for the specified phase numbers.
    /// Used for awards event eligible movies display.
    /// </summary>
    /// <param name="phaseNumbers">Array of phase numbers (e.g., [3, 4])</param>
    /// <returns>List of movie titles from those phases</returns>
    public async Task<List<string>> GetMovieNamesByPhasesAsync(int[] phaseNumbers)
    {
        try
        {
            // Convert to list for MongoDB query compatibility
            List<int> phaseList = phaseNumbers.ToList();

            // Query MongoDB for MovieEvents where PhaseNumber is in the list
            List<MovieEvent> events = await _repository.FindAsync(e =>
                e.PhaseNumber.HasValue &&
                phaseList.Contains(e.PhaseNumber.Value) &&
                !string.IsNullOrEmpty(e.Movie));

            // Extract movie titles and return
            return events
                .Select(e => e.Movie!)
                .Distinct()
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting movie names for phases {Phases}", string.Join(",", phaseNumbers));
            return new List<string>();
        }
    }

    /// <summary>
    /// Swaps person assignments between two months by creating/updating MovieEvent records.
    /// MovieEvent records override the cache, persisting swaps across server restarts.
    /// </summary>
    /// <param name="month1">First month to swap</param>
    /// <param name="month2">Second month to swap</param>
    public async Task SwapMonthAssignmentsAsync(DateTime month1, DateTime month2)
    {
        // Demo protection
        _demoProtection.ValidateNotDemo("Swap Month Assignment");

        // Validate: cannot swap same month
        if (month1.StartOfMonth() == month2.StartOfMonth())
        {
            throw new ArgumentException("Cannot swap a month with itself");
        }

        // Get cache assignments
        string? person1 = await _personAssignmentCache.GetPersonForMonthAsync(month1);
        string? person2 = await _personAssignmentCache.GetPersonForMonthAsync(month2);

        // Validate: both months must exist in cache
        if (string.IsNullOrEmpty(person1))
        {
            throw new InvalidOperationException($"Month {month1:yyyy-MM} not found in assignment cache");
        }

        if (string.IsNullOrEmpty(person2))
        {
            throw new InvalidOperationException($"Month {month2:yyyy-MM} not found in assignment cache");
        }

        // Validate: cannot swap awards months
        if (person1.StartsWith("Awards Event") || person2.StartsWith("Awards Event"))
        {
            throw new InvalidOperationException("Cannot swap awards months");
        }

        // Get or create MovieEvents for both months
        MovieEvent? event1 = await GetOrCreateForMonthAsync(month1);
        MovieEvent? event2 = await GetOrCreateForMonthAsync(month2);

        if (event1 == null || event2 == null)
        {
            throw new InvalidOperationException("Failed to create/retrieve MovieEvent records for swap");
        }

        // Swap Person fields (preserving all other data like Movie, IMDb, etc.)
        string tempPerson = event1.Person!;
        event1.Person = event2.Person!;
        event2.Person = tempPerson;

        // Update both events in database
        event1.UpdatedAt = DateTime.UtcNow;
        event2.UpdatedAt = DateTime.UtcNow;

        await UpdateAsync(event1);
        await UpdateAsync(event2);

        _logger.LogInformation(
            "Swapped month assignments: {Month1:yyyy-MM} ({Person1}) ↔ {Month2:yyyy-MM} ({Person2})",
            month1, event1.Person, month2, event2.Person);
    }
} 