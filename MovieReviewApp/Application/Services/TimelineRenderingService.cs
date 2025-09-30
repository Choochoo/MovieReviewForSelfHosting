using MovieReviewApp.Extensions;
using MovieReviewApp.Models;
using MovieReviewApp.Models.ViewModels;
using MovieReviewApp.Utilities;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Service for rendering timeline from PersonAssignmentCache (source of truth)
/// with database enrichment from MovieEvent records.
/// This eliminates dependency on Phase table.
/// </summary>
public class TimelineRenderingService
{
    private readonly PersonAssignmentCacheService _cache;
    private readonly MovieEventService _movieEventService;
    private readonly SettingService _settingService;
    private readonly PersonService _personService;
    private readonly ILogger<TimelineRenderingService> _logger;

    public TimelineRenderingService(
        PersonAssignmentCacheService cache,
        MovieEventService movieEventService,
        SettingService settingService,
        PersonService personService,
        ILogger<TimelineRenderingService> logger)
    {
        _cache = cache;
        _movieEventService = movieEventService;
        _settingService = settingService;
        _personService = personService;
        _logger = logger;
    }

    /// <summary>
    /// Builds complete timeline from cache with database enrichment
    /// </summary>
    public async Task<TimelineViewModel> BuildTimelineAsync()
    {
        try
        {
            // 1. Get cache (single source of truth)
            IReadOnlyDictionary<DateTime, string> cacheAssignments = await _cache.GetAllAssignmentsAsync();

            // 2. Get settings for phase calculation
            List<Setting> settings = await _settingService.GetAllAsync();
            Setting? startDateSetting = settings.FirstOrDefault(s => s.Key == "StartDate");

            if (startDateSetting == null || !DateTime.TryParse(startDateSetting.Value, out DateTime clubStartDate))
            {
                _logger.LogWarning("StartDate setting not found or invalid, using default");
                clubStartDate = new DateTime(2024, 3, 1); // Fallback
            }

            // 3. Get people count for phase boundary calculation
            List<Person> people = await _personService.GetAllAsync();
            int peoplePerPhase = people.Count > 0 ? people.Count : 6; // Fallback to 6

            // 4. Get ALL existing MovieEvents (one query)
            List<MovieEvent> allEvents = await _movieEventService.GetAllAsync();
            Dictionary<DateTime, MovieEvent> eventsByMonth = new Dictionary<DateTime, MovieEvent>();
            foreach (MovieEvent evt in allEvents)
            {
                DateTime monthKey = evt.StartDate.StartOfMonth();
                eventsByMonth[monthKey] = evt;
            }

            // 5. Build timeline items from cache
            DateTime now = DateProvider.Now;
            List<TimelineItem> allItems = BuildTimelineItems(cacheAssignments, eventsByMonth, now);

            // 6. Calculate phase boundaries (NO database lookup)
            List<TimelinePhase> phases = CalculatePhaseBoundaries(allItems, clubStartDate, peoplePerPhase, now);

            // 7. Return structured view model
            DateTime currentMonth = now.StartOfMonth();
            TimelineViewModel viewModel = new TimelineViewModel
            {
                CurrentPhase = phases.FirstOrDefault(p => p.IsCurrentPhase),
                FuturePhases = phases.Where(p => p.StartMonth > currentMonth).OrderBy(p => p.PhaseNumber).ToList(),
                PastPhases = phases.Where(p => p.EndMonth < currentMonth).OrderBy(p => p.PhaseNumber).ToList()
            };

            _logger.LogInformation("Timeline view model created:");
            _logger.LogInformation("  - Current Phase: {HasCurrent} (Phase {Number})",
                viewModel.CurrentPhase != null, viewModel.CurrentPhase?.PhaseNumber ?? 0);
            _logger.LogInformation("  - Future Phases: {Count} phases", viewModel.FuturePhases.Count);
            _logger.LogInformation("  - Past Phases: {Count} phases", viewModel.PastPhases.Count);

            foreach (TimelinePhase futurePhase in viewModel.FuturePhases.Take(3))
            {
                _logger.LogInformation("  Future Phase {PhaseNumber}: {StartMonth:yyyy-MM} to {EndMonth:yyyy-MM}, {ItemCount} items ({DbItems} with DB records)",
                    futurePhase.PhaseNumber, futurePhase.StartMonth, futurePhase.EndMonth,
                    futurePhase.Items.Count, futurePhase.Items.Count(i => i.MovieEventId.HasValue));
            }

            return viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building timeline from cache");
            throw;
        }
    }

    /// <summary>
    /// Builds timeline items from cache with database enrichment
    /// </summary>
    private List<TimelineItem> BuildTimelineItems(
        IReadOnlyDictionary<DateTime, string> cacheAssignments,
        Dictionary<DateTime, MovieEvent> existingEvents,
        DateTime now)
    {
        List<TimelineItem> items = new List<TimelineItem>();
        DateTime currentMonth = now.StartOfMonth();

        foreach (KeyValuePair<DateTime, string> assignment in cacheAssignments.OrderBy(a => a.Key))
        {
            string assignedPerson = assignment.Value;
            DateTime month = assignment.Key.StartOfMonth();

            // Check if awards event
            bool isAwards = assignedPerson.StartsWith("Awards Event");
            int? awardsNumber = null;
            if (isAwards)
            {
                string numStr = assignedPerson.Replace("Awards Event ", "").Trim();
                if (int.TryParse(numStr, out int num))
                    awardsNumber = num;
            }

            // Check if database record exists
            MovieEvent? dbEvent = existingEvents.GetValueOrDefault(month);

            // Determine state
            TimelineItemState state = month < currentMonth ? TimelineItemState.Past
                : month == currentMonth ? TimelineItemState.Current
                : TimelineItemState.Future;

            items.Add(new TimelineItem
            {
                Month = month,
                AssignedPersonName = isAwards ? "Awards Event" : assignedPerson,
                IsAwardsEvent = isAwards,
                AwardsEventNumber = awardsNumber,

                // Database enrichment
                MovieEventId = dbEvent?.Id,
                MovieTitle = dbEvent?.Movie,
                MoviePoster = dbEvent?.PosterUrl,
                HasRecording = false, // TODO: Add when MovieEvent has recording fields
                HasTranscription = false, // TODO: Add when MovieEvent has transcription fields

                State = state
            });
        }

        _logger.LogInformation("Built {Count} timeline items from cache with database enrichment", items.Count);
        return items;
    }

    /// <summary>
    /// Calculates phase boundaries from timeline items (no database queries)
    /// </summary>
    private List<TimelinePhase> CalculatePhaseBoundaries(
        List<TimelineItem> items,
        DateTime clubStartDate,
        int peoplePerPhase,
        DateTime now)
    {
        List<TimelinePhase> phases = new List<TimelinePhase>();
        TimelinePhase? currentPhase = null;
        int eventsInCurrentPhase = 0;
        int phaseCounter = 1;
        DateTime currentMonth = now.StartOfMonth();

        foreach (TimelineItem item in items.OrderBy(i => i.Month))
        {
            // Start new phase if:
            // 1. No current phase exists, OR
            // 2. Reached people count limit, OR
            // 3. Hit an awards event (awards create phase boundaries)
            if (currentPhase == null ||
                eventsInCurrentPhase >= peoplePerPhase ||
                (item.IsAwardsEvent && eventsInCurrentPhase > 0))
            {
                currentPhase = new TimelinePhase
                {
                    PhaseNumber = phaseCounter,
                    StartMonth = item.Month,
                    EndMonth = item.Month,
                    IsCurrentPhase = false
                };
                phases.Add(currentPhase);
                phaseCounter++;
                eventsInCurrentPhase = 0;
            }

            // Add item to phase
            currentPhase.Items.Add(item);
            currentPhase.EndMonth = item.Month;

            // Check if current phase
            if (item.Month == currentMonth)
                currentPhase.IsCurrentPhase = true;

            // Count non-awards events only for phase rotation
            if (!item.IsAwardsEvent)
            {
                eventsInCurrentPhase++;
                if (item.MovieEventId != null)
                    currentPhase.CompletedEvents++;
            }

            currentPhase.TotalEvents = currentPhase.Items.Count;
        }

        _logger.LogInformation("Calculated {Count} phase boundaries (people per phase: {PeoplePerPhase})",
            phases.Count, peoplePerPhase);

        return phases;
    }
}
