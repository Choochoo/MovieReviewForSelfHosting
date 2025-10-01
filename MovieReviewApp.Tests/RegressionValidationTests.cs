using Microsoft.Extensions.Logging;
using Moq;
using MovieReviewApp.Application.Services;
using MovieReviewApp.Extensions;
using MovieReviewApp.Models;
using MovieReviewApp.Models.ViewModels;
using MovieReviewApp.Utilities;
using Xunit;

namespace MovieReviewApp.Tests;

/// <summary>
/// Regression tests to ensure refactored timeline architecture doesn't break existing functionality.
/// Validates that person rotation algorithm remains deterministic and no regressions are introduced.
/// </summary>
public class RegressionValidationTests
{
    private readonly Mock<PersonAssignmentCacheService> _mockCache;
    private readonly Mock<MovieEventService> _mockMovieEventService;
    private readonly Mock<SettingService> _mockSettingService;
    private readonly Mock<PersonService> _mockPersonService;
    private readonly Mock<ILogger<TimelineRenderingService>> _mockLogger;

    public RegressionValidationTests()
    {
        _mockCache = new Mock<PersonAssignmentCacheService>();
        _mockMovieEventService = new Mock<MovieEventService>();
        _mockSettingService = new Mock<SettingService>();
        _mockPersonService = new Mock<PersonService>();
        _mockLogger = new Mock<ILogger<TimelineRenderingService>>();
    }

    [Fact]
    public async Task Regression_NoFlatteningOfPhaseGrouping()
    {
        // Arrange: Create test scenario with multiple phases
        DateTime clubStartDate = new DateTime(2024, 3, 1);
        List<Person> testPeople = CreateTestPeople(6);

        // Generate 18 months of assignments (3 complete phases)
        Dictionary<DateTime, string> cacheAssignments = new Dictionary<DateTime, string>();
        DateTime month = clubStartDate.StartOfMonth();

        for (int phaseNum = 0; phaseNum < 3; phaseNum++)
        {
            for (int personIdx = 0; personIdx < 6; personIdx++)
            {
                cacheAssignments[month] = testPeople[personIdx].Name!;
                month = month.AddMonths(1);
            }
        }

        // Setup mocks
        _mockCache
            .Setup(c => c.GetAllAssignmentsAsync())
            .ReturnsAsync(cacheAssignments);

        SetupDefaultMocks(clubStartDate, testPeople, new List<MovieEvent>());

        // Act: Build timeline
        TimelineRenderingService service = CreateService();
        TimelineViewModel timeline = await service.BuildTimelineAsync();

        // Assert: Verify TimelineViewModel structure is preserved (NOT flattened to List<ITimelineItem>)
        Assert.NotNull(timeline);
        Assert.IsType<TimelineViewModel>(timeline);

        // Timeline should have phase grouping structure
        List<TimelinePhase> allPhases = GetAllPhases(timeline);
        Assert.NotEmpty(allPhases);

        // Should have 3 phases (not a flat list of 18 items)
        Assert.True(allPhases.Count >= 3, $"Expected at least 3 phases, got {allPhases.Count}");

        // Each phase should have TimelineItems (not flattened MovieEvents)
        foreach (TimelinePhase phase in allPhases)
        {
            Assert.NotEmpty(phase.Items);
            Assert.All(phase.Items, item => Assert.IsType<TimelineItem>(item));
        }

        // Verify phase boundaries exist (phases should be distinct groups)
        List<int> phaseNumbers = allPhases.Select(p => p.PhaseNumber).Distinct().OrderBy(n => n).ToList();
        Assert.Equal(allPhases.Count, phaseNumbers.Count); // No duplicate phase numbers
    }

    [Fact]
    public void Regression_PersonRotationAlgorithm_StillDeterministic()
    {
        // Arrange: Use the same test data as PersonRotationServiceTests
        string[] testPeople = { "Alice", "Bob", "Charlie", "Diana" };
        DateTime testStartDate = new DateTime(2024, 1, 1);
        int monthsSinceStart = 2;

        // Act: Generate assignments twice with same parameters
        Dictionary<DateTime, string> assignments1 = PersonRotationService.GenerateGlobalPersonAssignments(
            testStartDate, testPeople, respectOrder: false, monthsSinceStart, awardSettings: null);

        Dictionary<DateTime, string> assignments2 = PersonRotationService.GenerateGlobalPersonAssignments(
            testStartDate, testPeople, respectOrder: false, monthsSinceStart, awardSettings: null);

        // Assert: Both runs should produce IDENTICAL results (deterministic)
        Assert.Equal(assignments1.Count, assignments2.Count);

        foreach (KeyValuePair<DateTime, string> kvp in assignments1)
        {
            // Check that second run has the key
            Assert.True(assignments2.ContainsKey(kvp.Key));

            // Check that values match
            Assert.Equal(kvp.Value, assignments2[kvp.Key]);
        }
    }

    [Fact]
    public void Regression_PersonRotationAlgorithm_OrderedMode_CorrectSequence()
    {
        // Arrange: Test ordered rotation (RespectOrder=true)
        string[] testPeople = { "Alice", "Bob", "Charlie", "Diana" };
        DateTime testStartDate = new DateTime(2024, 1, 1);

        // Act: Generate assignments for ordered mode
        Dictionary<DateTime, string> assignments = PersonRotationService.GenerateGlobalPersonAssignments(
            testStartDate, testPeople, respectOrder: true, monthsSinceStart: 0, awardSettings: null);

        // Assert: Should follow strict sequential order
        DateTime firstMonth = testStartDate.StartOfMonth();
        Assert.Equal("Alice", assignments[firstMonth]);
        Assert.Equal("Bob", assignments[firstMonth.AddMonths(1)]);
        Assert.Equal("Charlie", assignments[firstMonth.AddMonths(2)]);
        Assert.Equal("Diana", assignments[firstMonth.AddMonths(3)]);
        Assert.Equal("Alice", assignments[firstMonth.AddMonths(4)]); // Wraps around
    }

    /// <summary>
    /// Regression test: Random rotation produces valid, deterministic assignments.
    /// NOTE: Per claude.md - Consecutive duplicates CAN occur at phase boundaries (NOT a bug).
    /// Pool refill at phase start can randomly select same person as end of previous phase.
    /// </summary>
    [Fact]
    public void Regression_PersonRotationAlgorithm_RandomMode_ValidDeterministicAssignments()
    {
        // Arrange: Test random rotation (RespectOrder=false)
        string[] testPeople = { "Alice", "Bob", "Charlie", "Diana" };
        DateTime testStartDate = new DateTime(2024, 1, 1);

        // Act: Generate assignments for random mode
        Dictionary<DateTime, string> assignments = PersonRotationService.GenerateGlobalPersonAssignments(
            testStartDate, testPeople, respectOrder: false, monthsSinceStart: 0, awardSettings: null);

        // Assert: Verify deterministic behavior (same seed produces same results)
        Dictionary<DateTime, string> assignments2 = PersonRotationService.GenerateGlobalPersonAssignments(
            testStartDate, testPeople, respectOrder: false, monthsSinceStart: 0, awardSettings: null);

        Assert.Equal(assignments.Count, assignments2.Count);
        foreach (KeyValuePair<DateTime, string> kvp in assignments)
        {
            Assert.Equal(kvp.Value, assignments2[kvp.Key]);
        }

        // Verify all assignments are from valid people list
        DateTime currentMonth = testStartDate.StartOfMonth();
        for (int i = 0; i < 12; i++)
        {
            string currentPerson = assignments[currentMonth];
            Assert.Contains(currentPerson, testPeople);
            currentMonth = currentMonth.AddMonths(1);
        }
    }

    [Fact]
    public async Task Regression_TimelineRenderingService_HandlesMissingSettings()
    {
        // Arrange: Missing StartDate setting (should use fallback)
        List<Person> testPeople = CreateTestPeople(6);

        Dictionary<DateTime, string> cacheAssignments = new Dictionary<DateTime, string>
        {
            { new DateTime(2024, 3, 1), "Alice" }
        };

        _mockCache
            .Setup(c => c.GetAllAssignmentsAsync())
            .ReturnsAsync(cacheAssignments);

        // Mock with EMPTY settings (missing StartDate)
        _mockSettingService
            .Setup(s => s.GetAllAsync())
            .ReturnsAsync(new List<Setting>());

        _mockPersonService
            .Setup(p => p.GetAllAsync())
            .ReturnsAsync(testPeople);

        _mockMovieEventService
            .Setup(m => m.GetAllAsync())
            .ReturnsAsync(new List<MovieEvent>());

        // Act: Should not throw exception (should use fallback)
        TimelineRenderingService service = CreateService();
        TimelineViewModel timeline = await service.BuildTimelineAsync();

        // Assert: Should return valid timeline (not crash)
        Assert.NotNull(timeline);

        // Should have created phases using fallback start date
        List<TimelinePhase> allPhases = GetAllPhases(timeline);
        Assert.NotEmpty(allPhases);
    }

    [Fact]
    public async Task Regression_TimelineRenderingService_HandlesEmptyPeopleList()
    {
        // Arrange: Empty people list (edge case)
        Dictionary<DateTime, string> cacheAssignments = new Dictionary<DateTime, string>();

        _mockCache
            .Setup(c => c.GetAllAssignmentsAsync())
            .ReturnsAsync(cacheAssignments);

        SetupDefaultMocks(new DateTime(2024, 3, 1), new List<Person>(), new List<MovieEvent>());

        // Act: Should not throw exception
        TimelineRenderingService service = CreateService();
        TimelineViewModel timeline = await service.BuildTimelineAsync();

        // Assert: Should return empty timeline (not crash)
        Assert.NotNull(timeline);
        Assert.Null(timeline.CurrentPhase);
        Assert.Empty(timeline.FuturePhases);
        Assert.Empty(timeline.PastPhases);
    }

    [Fact]
    public async Task Regression_TimelineRenderingService_PreservesDatabasePriority()
    {
        // Arrange: Cache and database have conflicting data - database should win
        DateTime clubStartDate = new DateTime(2024, 3, 1);
        List<Person> testPeople = CreateTestPeople(6);

        // Cache says "Alice" for March 2024
        Dictionary<DateTime, string> cacheAssignments = new Dictionary<DateTime, string>
        {
            { clubStartDate, "Alice" }
        };

        // Database says "Bob" for March 2024 (different from cache)
        List<MovieEvent> existingEvents = new List<MovieEvent>
        {
            new MovieEvent
            {
                Id = Guid.NewGuid(),
                StartDate = clubStartDate,
                EndDate = clubStartDate.AddDays(29),
                Person = "Bob", // Database value (should override cache)
                Movie = "Test Movie",
                PhaseNumber = 1
            }
        };

        _mockCache
            .Setup(c => c.GetAllAssignmentsAsync())
            .ReturnsAsync(cacheAssignments);

        SetupDefaultMocks(clubStartDate, testPeople, existingEvents);

        // Act: Build timeline
        TimelineRenderingService service = CreateService();
        TimelineViewModel timeline = await service.BuildTimelineAsync();

        // Assert: Database value should be used (not cache)
        List<TimelinePhase> allPhases = GetAllPhases(timeline);
        TimelineItem? marchItem = allPhases
            .SelectMany(p => p.Items)
            .FirstOrDefault(i => i.Month.Year == 2024 && i.Month.Month == 3);

        Assert.NotNull(marchItem);

        // Database person assignment should take precedence over cache
        Assert.Equal("Bob", marchItem.AssignedPersonName); // Database value (not cache "Alice")
        Assert.NotNull(marchItem.MovieEventId); // Database provides enrichment
        Assert.Equal("Test Movie", marchItem.MovieTitle); // Database provides movie details
    }

    [Fact]
    public async Task Regression_TimelineRenderingService_CorrectlyCalculatesPhaseNumbers()
    {
        // Arrange: Generate 3 phases (18 months with 6 people)
        DateTime clubStartDate = new DateTime(2024, 3, 1);
        List<Person> testPeople = CreateTestPeople(6);

        Dictionary<DateTime, string> cacheAssignments = new Dictionary<DateTime, string>();
        DateTime month = clubStartDate.StartOfMonth();

        // Generate 18 months (3 complete phases)
        for (int i = 0; i < 18; i++)
        {
            cacheAssignments[month] = testPeople[i % 6].Name!;
            month = month.AddMonths(1);
        }

        _mockCache
            .Setup(c => c.GetAllAssignmentsAsync())
            .ReturnsAsync(cacheAssignments);

        SetupDefaultMocks(clubStartDate, testPeople, new List<MovieEvent>());

        // Act: Build timeline
        TimelineRenderingService service = CreateService();
        TimelineViewModel timeline = await service.BuildTimelineAsync();

        // Assert: Phase numbers should be sequential (1, 2, 3)
        List<TimelinePhase> allPhases = GetAllPhases(timeline);
        List<int> phaseNumbers = allPhases.Select(p => p.PhaseNumber).OrderBy(n => n).ToList();

        // Verify sequential numbering (no gaps)
        for (int i = 1; i < phaseNumbers.Count; i++)
        {
            int expected = phaseNumbers[i - 1] + 1;
            // Verify sequential phase numbers
            Assert.Equal(expected, phaseNumbers[i]);
        }

        // First phase should start at 1 (not 0)
        Assert.Equal(1, phaseNumbers.First());
    }

    [Fact]
    public async Task Regression_AwardsDoNotCreatePhaseGaps()
    {
        // Arrange: Generate scenario with awards (PhasesBeforeAward=2)
        DateTime clubStartDate = new DateTime(2024, 3, 1);
        List<Person> testPeople = CreateTestPeople(6);

        // Generate cache with 2 phases + awards event + 1 more phase
        // Phase 1: Mar-Aug 2024 (6 months)
        // Phase 2: Sep 2024-Feb 2025 (6 months)
        // Awards Event 1: Mar 2025
        // Phase 3: Apr-Sep 2025 (6 months) ← Should be Phase 3, NOT Phase 4
        Dictionary<DateTime, string> cacheAssignments = new Dictionary<DateTime, string>();
        DateTime month = clubStartDate.StartOfMonth();

        // Phase 1 (6 events)
        for (int i = 0; i < 6; i++)
        {
            cacheAssignments[month] = testPeople[i].Name!;
            month = month.AddMonths(1);
        }

        // Phase 2 (6 events)
        for (int i = 0; i < 6; i++)
        {
            cacheAssignments[month] = testPeople[i].Name!;
            month = month.AddMonths(1);
        }

        // Awards Event 1 (Mar 2025)
        cacheAssignments[month] = "Awards Event 1";
        month = month.AddMonths(1);

        // Phase 3 (6 events)
        for (int i = 0; i < 6; i++)
        {
            cacheAssignments[month] = testPeople[i].Name!;
            month = month.AddMonths(1);
        }

        _mockCache
            .Setup(c => c.GetAllAssignmentsAsync())
            .ReturnsAsync(cacheAssignments);

        SetupDefaultMocks(clubStartDate, testPeople, new List<MovieEvent>());

        // Act: Build timeline
        TimelineRenderingService service = CreateService();
        TimelineViewModel timeline = await service.BuildTimelineAsync();

        // Assert: Phase numbers should be 1, 2, 3 (no gap for awards)
        List<TimelinePhase> allPhases = GetAllPhases(timeline);
        List<int> phaseNumbers = allPhases
            .Where(p => p.Items.Any(i => !i.IsAwardsEvent)) // Only phases with regular events
            .Select(p => p.PhaseNumber)
            .OrderBy(n => n)
            .ToList();

        // Should have exactly 3 phases (not 4)
        Assert.True(phaseNumbers.Count >= 3, $"Expected at least 3 phases, got {phaseNumbers.Count}");

        // Phase numbers should be sequential: 1, 2, 3 (NOT 1, 2, 4)
        Assert.Equal(1, phaseNumbers[0]);
        Assert.Equal(2, phaseNumbers[1]);
        Assert.Equal(3, phaseNumbers[2]); // CRITICAL: Should be 3, not 4!

        // Verify awards event exists and is associated with correct phase
        TimelineItem? awardsItem = allPhases
            .SelectMany(p => p.Items)
            .FirstOrDefault(i => i.IsAwardsEvent && i.AwardsEventNumber == 1);

        Assert.NotNull(awardsItem);
        Assert.Equal(new DateTime(2025, 3, 1), awardsItem.Month);
    }

    #region Helper Methods

    private List<Person> CreateTestPeople(int count)
    {
        List<Person> people = new List<Person>();
        string[] names = { "Alice", "Bob", "Charlie", "Diana", "Eve", "Frank", "Grace", "Henry" };

        for (int i = 0; i < count; i++)
        {
            people.Add(new Person
            {
                Id = Guid.NewGuid(),
                Name = names[i % names.Length],
                Order = i + 1
            });
        }

        return people;
    }

    private void SetupDefaultMocks(DateTime startDate, List<Person> people, List<MovieEvent> events)
    {
        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = startDate.ToString("o") }
        };

        _mockSettingService
            .Setup(s => s.GetAllAsync())
            .ReturnsAsync(settings);

        _mockPersonService
            .Setup(p => p.GetAllAsync())
            .ReturnsAsync(people);

        _mockMovieEventService
            .Setup(m => m.GetAllAsync())
            .ReturnsAsync(events);
    }

    private TimelineRenderingService CreateService()
    {
        return new TimelineRenderingService(
            _mockCache.Object,
            _mockMovieEventService.Object,
            _mockSettingService.Object,
            _mockPersonService.Object,
            _mockLogger.Object
        );
    }

    private List<TimelinePhase> GetAllPhases(TimelineViewModel timeline)
    {
        List<TimelinePhase> allPhases = new List<TimelinePhase>();
        if (timeline.CurrentPhase != null)
            allPhases.Add(timeline.CurrentPhase);
        allPhases.AddRange(timeline.FuturePhases);
        allPhases.AddRange(timeline.PastPhases);
        return allPhases;
    }

    #endregion
}
