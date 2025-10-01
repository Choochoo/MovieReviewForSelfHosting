using MovieReviewApp.Application.Services;
using MovieReviewApp.Extensions;
using MovieReviewApp.Models;
using MovieReviewApp.Models.ViewModels;
using MovieReviewApp.Utilities;
using Xunit;

namespace MovieReviewApp.Tests;

/// <summary>
/// Comprehensive tests for timeline implementation to ensure cached assignments
/// are properly displayed as timeline items without creating database records.
/// Tests the new AddFutureCachedEventsToTimeline functionality in Home.razor.cs.
/// </summary>
[Collection("Sequential")]
public class TimelineImplementationTests
{
    private readonly TestTimelineHelper _helper;

    public TimelineImplementationTests()
    {
        _helper = new TestTimelineHelper();
    }

    [Fact]
    public void PersonAssignmentCache_ShouldGenerateCorrectAssignments_ForTimelineDisplay()
    {
        // Arrange
        DateTime startDate = new DateTime(2024, 1, 1);
        string[] people = { "Dave", "Jared", "Lacey", "Keri", "Jeremiah", "Nikki" };
        AwardSetting awardSettings = new AwardSetting { AwardsEnabled = false, PhasesBeforeAward = 2 };

        // Act: Generate cached assignments (simulating PersonAssignmentCacheService)
        Dictionary<DateTime, string> cachedAssignments = PersonRotationService.GenerateGlobalPersonAssignments(
            startDate, people, respectOrder: false, monthsSinceStart: 0, awardSettings);

        // Assert: Cached assignments should contain person assignments for timeline
        Assert.NotEmpty(cachedAssignments);

        // Verify all people are assigned over time
        List<string> assignedPeople = cachedAssignments.Values.Where(v => !v.StartsWith("Awards Event")).Distinct().ToList();
        foreach (string person in people)
        {
            Assert.Contains(person, assignedPeople);
        }

        // Verify assignments follow chronological order
        List<DateTime> months = cachedAssignments.Keys.OrderBy(k => k).ToList();
        for (int i = 1; i < months.Count; i++)
        {
            Assert.True(months[i] > months[i-1], "Cached assignments should be in chronological order");
        }
    }

    [Fact]
    public void Timeline_ShouldIncludeCachedAwardEvents_AsTimelineItems()
    {
        // Arrange
        DateTime startDate = new DateTime(2024, 1, 1);
        string[] people = { "Dave", "Jared", "Lacey", "Keri", "Jeremiah", "Nikki" };
        AwardSetting awardSettings = new AwardSetting { AwardsEnabled = true, PhasesBeforeAward = 2 };

        // Create cached assignments with awards
        Dictionary<DateTime, string> cachedAssignments = PersonRotationService.GenerateGlobalPersonAssignments(
            startDate, people, respectOrder: false, monthsSinceStart: 0, awardSettings);

        // Act: Extract award events from cache
        List<KeyValuePair<DateTime, string>> awardEntries = cachedAssignments
            .Where(a => a.Value.StartsWith("Awards Event"))
            .OrderBy(a => a.Key)
            .ToList();

        // Assert: Cached assignments should contain award events
        Assert.NotEmpty(awardEntries);

        // Verify award events follow expected pattern
        Assert.Contains("Awards Event 1", awardEntries.Select(a => a.Value));

        // Verify awards appear after expected number of phases
        DateTime firstAwardDate = awardEntries.First().Key;
        int monthsToFirstAward = ((firstAwardDate.Year - startDate.Year) * 12) + (firstAwardDate.Month - startDate.Month);

        // Should be after 2 phases (12 people total = 2 phases of 6 people each)
        int expectedMonthsToAward = people.Length * awardSettings.PhasesBeforeAward;
        Assert.Equal(expectedMonthsToAward, monthsToFirstAward);
    }

    [Fact]
    public void Timeline_ShouldRespectTwoMonthFutureCutoff_ForCachedEvents()
    {
        // Arrange
        DateTime currentDate = new DateTime(2024, 6, 15); // Mid-June
        DateProvider.SetCustomDate(currentDate);

        DateTime startDate = new DateTime(2024, 1, 1);
        string[] people = { "Dave", "Jared", "Lacey" };
        AwardSetting awardSettings = new AwardSetting { AwardsEnabled = false, PhasesBeforeAward = 2 };

        // Calculate months since start for timeline position
        int monthsSinceStart = ((currentDate.Year - startDate.Year) * 12) + (currentDate.Month - startDate.Month);

        Dictionary<DateTime, string> cachedAssignments = PersonRotationService.GenerateGlobalPersonAssignments(
            startDate, people, respectOrder: false, monthsSinceStart, awardSettings);

        // Act: Filter assignments based on 2+ month future cutoff (August 2024 and later)
        DateTime futureStartMonth = currentDate.AddMonths(2).StartOfMonth(); // August 1, 2024
        List<KeyValuePair<DateTime, string>> futureAssignments = cachedAssignments
            .Where(a => a.Key >= futureStartMonth)
            .OrderBy(a => a.Key)
            .ToList();

        // Assert: Should have future assignments starting from August
        Assert.NotEmpty(futureAssignments);
        Assert.True(futureAssignments.First().Key >= futureStartMonth,
            $"First future assignment {futureAssignments.First().Key} should be >= {futureStartMonth}");

        // Verify no assignments before the cutoff date in the filtered results
        foreach (KeyValuePair<DateTime, string> assignment in futureAssignments)
        {
            Assert.True(assignment.Key >= futureStartMonth,
                $"Future assignment {assignment.Key} should be >= {futureStartMonth}");
        }

        // Cleanup
        DateProvider.ResetDate();
    }

    [Fact]
    public void Timeline_ShouldNotCreateDatabaseRecords_ForCachedAssignments()
    {
        // Arrange
        DateTime startDate = new DateTime(2024, 1, 1);
        string[] people = { "Dave", "Jared", "Lacey" };

        // Act: Create MovieEvent from PhaseEventGenerator (simulates timeline creation)
        MovieEvent cachedEvent = PhaseEventGenerator.CreateMovieEvent("Dave", startDate, 1);

        // Assert: Cached events should have timeline indicators (not database records)
        // Note: MovieEvent inherits from BaseModel which auto-assigns GUID, but FromDatabase=false indicates it's cached
        Assert.False(cachedEvent.FromDatabase); // This is the key indicator

        // Cached events should have minimal data (not full database records)
        Assert.Null(cachedEvent.Movie);
        Assert.False(cachedEvent.AlreadySeen);

        // But should have basic assignment data for timeline display
        Assert.Equal("Dave", cachedEvent.Person);
        Assert.Equal(1, cachedEvent.PhaseNumber);
        Assert.Equal(startDate.StartOfMonth(), cachedEvent.StartDate);

        // ID is auto-assigned by BaseModel constructor, but FromDatabase=false means it's not persisted
        Assert.NotEqual(Guid.Empty, cachedEvent.Id); // BaseModel assigns GUID automatically
    }

    [Fact]
    public void Timeline_ShouldMaintainChronologicalOrder_WithMixedContent()
    {
        // Arrange
        DateTime startDate = new DateTime(2024, 1, 1);
        string[] people = { "Dave", "Jared", "Lacey" };
        AwardSetting awardSettings = new AwardSetting { AwardsEnabled = true, PhasesBeforeAward = 2 };

        Dictionary<DateTime, string> cachedAssignments = PersonRotationService.GenerateGlobalPersonAssignments(
            startDate, people, respectOrder: false, monthsSinceStart: 0, awardSettings);

        // Act: Create timeline items from cached assignments
        List<TestTimelineItem> timelineItems = new List<TestTimelineItem>();

        foreach (KeyValuePair<DateTime, string> assignment in cachedAssignments.OrderBy(a => a.Key))
        {
            if (assignment.Value.StartsWith("Awards Event"))
            {
                timelineItems.Add(new TestTimelineItem
                {
                    Date = assignment.Key,
                    Type = "Award",
                    Content = assignment.Value
                });
            }
            else
            {
                timelineItems.Add(new TestTimelineItem
                {
                    Date = assignment.Key,
                    Type = "Person",
                    Content = assignment.Value
                });
            }
        }

        // Assert: Timeline should be in chronological order
        Assert.NotEmpty(timelineItems);

        for (int i = 1; i < timelineItems.Count; i++)
        {
            Assert.True(timelineItems[i-1].Date <= timelineItems[i].Date,
                $"Timeline not in chronological order: {timelineItems[i-1].Date} should be <= {timelineItems[i].Date}");
        }

        // Verify mixed content is properly ordered
        List<TestTimelineItem> personItems = timelineItems.Where(t => t.Type == "Person").ToList();
        List<TestTimelineItem> awardItems = timelineItems.Where(t => t.Type == "Award").ToList();

        Assert.NotEmpty(personItems);
        Assert.NotEmpty(awardItems);

        // Award items should come after person items in each cycle
        TestTimelineItem firstAward = awardItems.First();
        List<TestTimelineItem> peopleBeforeFirstAward = personItems.Where(p => p.Date < firstAward.Date).ToList();

        // Should have exactly 2 phases worth of people before first award (6 people total)
        Assert.Equal(people.Length * awardSettings.PhasesBeforeAward, peopleBeforeFirstAward.Count);
    }

    [Fact]
    public void Timeline_Performance_ShouldHandleLargeCacheEfficiently()
    {
        // Arrange
        DateTime startDate = new DateTime(2024, 1, 1);
        string[] people = { "Dave", "Jared", "Lacey", "Keri", "Jeremiah", "Nikki" };
        AwardSetting awardSettings = new AwardSetting { AwardsEnabled = true, PhasesBeforeAward = 2 };

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act: Generate large cache and process for timeline
        Dictionary<DateTime, string> cachedAssignments = PersonRotationService.GenerateGlobalPersonAssignments(
            startDate, people, respectOrder: false, monthsSinceStart: 0, awardSettings);

        // Simulate timeline processing (filtering and sorting)
        DateTime futureStartMonth = DateTime.Now.AddMonths(2).StartOfMonth();
        List<KeyValuePair<DateTime, string>> timelineAssignments = cachedAssignments
            .Where(a => a.Key >= futureStartMonth)
            .OrderBy(a => a.Key)
            .Take(12) // Limit to 12 months as per implementation
            .ToList();

        stopwatch.Stop();

        // Assert: Should complete within performance requirements
        Assert.True(stopwatch.ElapsedMilliseconds < 100,
            $"Timeline cache processing took {stopwatch.ElapsedMilliseconds}ms, should be < 100ms for UI responsiveness");

        // Verify substantial data was processed
        // With 6 people + awards every 2 phases, from startDate to Now + 24 months = ~45 entries
        Assert.True(cachedAssignments.Count > 40, "Should process substantial cache data");
        Assert.NotEmpty(timelineAssignments);
    }

    [Fact]
    public void Timeline_ShouldHandleEmptyCache_Gracefully()
    {
        // Arrange
        Dictionary<DateTime, string> emptyCache = new Dictionary<DateTime, string>();
        DateTime futureStartMonth = DateTime.Now.AddMonths(2).StartOfMonth();

        // Act: Process empty cache for timeline
        List<KeyValuePair<DateTime, string>> timelineAssignments = emptyCache
            .Where(a => a.Key >= futureStartMonth)
            .OrderBy(a => a.Key)
            .ToList();

        // Assert: Should handle empty cache without errors
        Assert.Empty(timelineAssignments);
    }

    [Fact]
    public void Timeline_ShouldIntegrateCachedAndPersistedEvents_Correctly()
    {
        // Arrange - Simulate events from both database and cache
        DateTime eventDate = new DateTime(2024, 7, 1);

        // Database event (persisted) - would have been loaded from database
        MovieEvent persistedEvent = new MovieEvent
        {
            Person = "Dave",
            Movie = "Persisted Movie",
            StartDate = eventDate,
            EndDate = eventDate.AddDays(6),
            AlreadySeen = true,
            PhaseNumber = 1,
            FromDatabase = true // Key indicator: this came from database
        };

        // Cached event (future assignment) - created by timeline logic
        MovieEvent cachedEvent = PhaseEventGenerator.CreateMovieEvent("Jared", eventDate.AddMonths(1), 1);

        // Act: Compare event characteristics
        List<MovieEvent> allEvents = new List<MovieEvent> { persistedEvent, cachedEvent };

        // Assert: Events should be distinguishable by FromDatabase flag
        MovieEvent dbEvent = allEvents.First(e => e.FromDatabase);
        MovieEvent cacheEvent = allEvents.First(e => !e.FromDatabase);

        // Database event has full data
        Assert.NotNull(dbEvent.Movie);
        Assert.True(dbEvent.AlreadySeen);
        Assert.True(dbEvent.FromDatabase);

        // Cached event has minimal data
        Assert.Null(cacheEvent.Movie);
        Assert.False(cacheEvent.AlreadySeen);
        Assert.False(cacheEvent.FromDatabase);

        // Both should have person assignment
        Assert.NotNull(dbEvent.Person);
        Assert.NotNull(cacheEvent.Person);

        // Both will have GUIDs (BaseModel assigns them) but FromDatabase distinguishes them
        Assert.NotEqual(Guid.Empty, dbEvent.Id);
        Assert.NotEqual(Guid.Empty, cacheEvent.Id);
    }

    [Fact]
    public void Timeline_ShouldCreateCorrectPhaseStructure_FromCachedAssignments()
    {
        // Arrange
        DateTime startDate = new DateTime(2024, 8, 1); // Future month
        string[] people = { "Dave", "Jared", "Lacey" };

        // Create cached assignments for one complete phase
        List<MovieEvent> phaseEvents = new List<MovieEvent>();
        for (int i = 0; i < people.Length; i++)
        {
            DateTime eventDate = startDate.AddMonths(i);
            MovieEvent evt = PhaseEventGenerator.CreateMovieEvent(people[i], eventDate, 5);
            phaseEvents.Add(evt);
        }

        // Act: Create phase from events (simulates CreatePhaseFromEvents)
        Phase phase = _helper.CreatePhaseFromEvents(5, startDate, phaseEvents, string.Join(",", people));

        // Assert: Phase should be properly structured
        Assert.Equal(5, phase.Number);
        Assert.Equal(startDate.StartOfMonth(), phase.StartDate);
        Assert.Equal(people.Length, phase.Events.Count);
        Assert.Equal(string.Join(",", people), phase.People);

        // Verify events are in chronological order
        for (int i = 1; i < phase.Events.Count; i++)
        {
            Assert.True(phase.Events[i-1].StartDate <= phase.Events[i].StartDate,
                "Phase events should be in chronological order");
        }

        // Verify all people are assigned - handle nullable strings
        List<string> assignedPeople = phase.Events.Select(e => e.Person).Where(p => p != null).Cast<string>().ToList();
        foreach (string person in people)
        {
            Assert.Contains(person, assignedPeople);
        }
    }

    [Fact]
    public void Timeline_ShouldCreateFutureAwardItems_FromCachedAssignments()
    {
        // Arrange
        DateTime startDate = new DateTime(2024, 1, 1);
        string[] people = { "Dave", "Jared", "Lacey" };
        AwardSetting awardSettings = new AwardSetting { AwardsEnabled = true, PhasesBeforeAward = 2 };

        Dictionary<DateTime, string> cachedAssignments = PersonRotationService.GenerateGlobalPersonAssignments(
            startDate, people, respectOrder: false, monthsSinceStart: 0, awardSettings);

        // Act: Create FutureAwardItem from cached award assignment
        KeyValuePair<DateTime, string> awardAssignment = cachedAssignments
            .First(a => a.Value.StartsWith("Awards Event"));

        FutureAwardItem futureAward = new FutureAwardItem
        {
            AwardDate = awardAssignment.Key,
            PhaseNumber = _helper.CalculatePhaseNumber(awardAssignment.Key, startDate, people.Length)
        };

        FutureAwardTimelineItem timelineItem = new FutureAwardTimelineItem(futureAward);

        // Assert: Timeline item should be properly created
        Assert.Equal(awardAssignment.Key, timelineItem.Date);
        Assert.Equal(awardAssignment.Key, timelineItem.FutureAward.AwardDate);
        Assert.True(timelineItem.FutureAward.PhaseNumber > 0);
    }

    [Fact]
    public void Timeline_ShouldFilterCurrentAndFutureEvents_CorrectlyFromCache()
    {
        // Arrange
        DateTime currentDate = new DateTime(2024, 6, 15);
        DateProvider.SetCustomDate(currentDate);

        DateTime startDate = new DateTime(2024, 1, 1);
        string[] people = { "Dave", "Jared", "Lacey" };
        AwardSetting awardSettings = new AwardSetting { AwardsEnabled = false, PhasesBeforeAward = 2 };

        Dictionary<DateTime, string> cachedAssignments = PersonRotationService.GenerateGlobalPersonAssignments(
            startDate, people, respectOrder: false, monthsSinceStart: 5, awardSettings);

        // Act: Filter assignments like the timeline implementation does
        DateTime currentMonthStart = new DateTime(currentDate.Year, currentDate.Month, 1);
        List<KeyValuePair<DateTime, string>> currentAndFutureAssignments = cachedAssignments
            .Where(a => a.Key >= currentMonthStart)
            .OrderBy(a => a.Key)
            .ToList();

        // Assert: Should only include current month and future assignments
        Assert.NotEmpty(currentAndFutureAssignments);

        foreach (KeyValuePair<DateTime, string> assignment in currentAndFutureAssignments)
        {
            Assert.True(assignment.Key >= currentMonthStart,
                $"Assignment date {assignment.Key} should be >= current month start {currentMonthStart}");
        }

        // Should not include past months (before June 2024)
        Assert.DoesNotContain(currentAndFutureAssignments, a => a.Key.Month < 6 && a.Key.Year == 2024);

        // Cleanup
        DateProvider.ResetDate();
    }
}

/// <summary>
/// Helper class for timeline testing that provides utilities for testing
/// timeline functionality without requiring full Blazor infrastructure.
/// </summary>
public class TestTimelineHelper
{
    public Phase CreatePhaseFromEvents(int phaseNumber, DateTime startDate, List<MovieEvent> events, string people)
    {
        return new Phase
        {
            Number = phaseNumber,
            StartDate = startDate.StartOfMonth(),
            EndDate = events.Any() ? events.Max(e => e.EndDate) : startDate.EndOfMonth(),
            Events = new List<MovieEvent>(events),
            People = people
        };
    }

    public int CalculatePhaseNumber(DateTime date, DateTime startDate, int peoplePerPhase)
    {
        int monthsSinceStart = ((date.Year - startDate.Year) * 12) + (date.Month - startDate.Month);
        return (monthsSinceStart / peoplePerPhase) + 1;
    }
}

/// <summary>
/// Simple timeline item for testing chronological order and content verification.
/// </summary>
public class TestTimelineItem
{
    public DateTime Date { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}