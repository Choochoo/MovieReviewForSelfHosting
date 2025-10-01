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
/// Integration tests for complete data flow from PersonAssignmentCache through TimelineRenderingService.
/// Validates that phase grouping is preserved end-to-end and not flattened.
/// </summary>
public class PhaseGroupingIntegrationTests
{
    private readonly Mock<PersonAssignmentCacheService> _mockCache;
    private readonly Mock<MovieEventService> _mockMovieEventService;
    private readonly Mock<SettingService> _mockSettingService;
    private readonly Mock<PersonService> _mockPersonService;
    private readonly Mock<ILogger<TimelineRenderingService>> _mockLogger;

    public PhaseGroupingIntegrationTests()
    {
        _mockCache = new Mock<PersonAssignmentCacheService>();
        _mockMovieEventService = new Mock<MovieEventService>();
        _mockSettingService = new Mock<SettingService>();
        _mockPersonService = new Mock<PersonService>();
        _mockLogger = new Mock<ILogger<TimelineRenderingService>>();
    }

    [Fact]
    public async Task CompleteDataFlow_CacheToUI_PreservesPhaseGrouping()
    {
        // Arrange: Create test data with 6 people (1 phase = 6 months)
        DateTime clubStartDate = new DateTime(2024, 3, 1);
        List<Person> testPeople = new List<Person>
        {
            new Person { Id = Guid.NewGuid(), Name = "Alice", Order = 1 },
            new Person { Id = Guid.NewGuid(), Name = "Bob", Order = 2 },
            new Person { Id = Guid.NewGuid(), Name = "Charlie", Order = 3 },
            new Person { Id = Guid.NewGuid(), Name = "Diana", Order = 4 },
            new Person { Id = Guid.NewGuid(), Name = "Eve", Order = 5 },
            new Person { Id = Guid.NewGuid(), Name = "Frank", Order = 6 }
        };

        // Create cache assignments (ordered rotation for deterministic testing)
        Dictionary<DateTime, string> cacheAssignments = new Dictionary<DateTime, string>();
        DateTime currentMonth = clubStartDate.StartOfMonth();

        // Phase 1: March - August 2024 (6 months)
        for (int i = 0; i < 6; i++)
        {
            cacheAssignments[currentMonth] = testPeople[i].Name!;
            currentMonth = currentMonth.AddMonths(1);
        }

        // Phase 2: September 2024 - February 2025 (6 months)
        for (int i = 0; i < 6; i++)
        {
            cacheAssignments[currentMonth] = testPeople[i].Name!;
            currentMonth = currentMonth.AddMonths(1);
        }

        // Mock cache service
        _mockCache
            .Setup(c => c.GetAllAssignmentsAsync())
            .ReturnsAsync(cacheAssignments);

        // Mock settings
        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = clubStartDate.ToString("o") }
        };
        _mockSettingService
            .Setup(s => s.GetAllAsync())
            .ReturnsAsync(settings);

        // Mock person service
        _mockPersonService
            .Setup(p => p.GetAllAsync())
            .ReturnsAsync(testPeople);

        // Mock movie events (only first 3 months have database records)
        List<MovieEvent> existingEvents = new List<MovieEvent>
        {
            new MovieEvent
            {
                Id = Guid.NewGuid(),
                StartDate = new DateTime(2024, 3, 1),
                EndDate = new DateTime(2024, 3, 31),
                Person = "Alice",
                PhaseNumber = 1
            },
            new MovieEvent
            {
                Id = Guid.NewGuid(),
                StartDate = new DateTime(2024, 4, 1),
                EndDate = new DateTime(2024, 4, 30),
                Person = "Bob",
                PhaseNumber = 1
            },
            new MovieEvent
            {
                Id = Guid.NewGuid(),
                StartDate = new DateTime(2024, 5, 1),
                EndDate = new DateTime(2024, 5, 31),
                Person = "Charlie",
                PhaseNumber = 1
            }
        };
        _mockMovieEventService
            .Setup(m => m.GetAllAsync())
            .ReturnsAsync(existingEvents);

        // Act: Build timeline using TimelineRenderingService
        TimelineRenderingService service = new TimelineRenderingService(
            _mockCache.Object,
            _mockMovieEventService.Object,
            _mockSettingService.Object,
            _mockPersonService.Object,
            _mockLogger.Object
        );

        TimelineViewModel timeline = await service.BuildTimelineAsync();

        // Assert: Verify phase grouping is preserved (NOT flattened)
        Assert.NotNull(timeline);

        // Should have multiple phases (not a flat list)
        int totalPhases = (timeline.CurrentPhase != null ? 1 : 0) +
                         timeline.FuturePhases.Count +
                         timeline.PastPhases.Count;

        Assert.True(totalPhases >= 2, $"Expected at least 2 phases, but got {totalPhases}");

        // Current phase should exist (one of the first 3 months should be current/past depending on DateProvider.Now)
        // and should contain correct number of items
        List<TimelinePhase> allPhases = new List<TimelinePhase>();
        if (timeline.CurrentPhase != null)
            allPhases.Add(timeline.CurrentPhase);
        allPhases.AddRange(timeline.FuturePhases);
        allPhases.AddRange(timeline.PastPhases);

        // Each phase should have <= 6 items (people count)
        foreach (TimelinePhase phase in allPhases)
        {
            Assert.True(phase.Items.Count <= 6,
                $"Phase {phase.PhaseNumber} has {phase.Items.Count} items, expected <= 6");
            Assert.True(phase.Items.Count > 0,
                $"Phase {phase.PhaseNumber} should have at least 1 item");
        }

        // Verify chronological ordering within phases
        foreach (TimelinePhase phase in allPhases)
        {
            DateTime? previousMonth = null;
            foreach (TimelineItem item in phase.Items)
            {
                if (previousMonth.HasValue)
                {
                    Assert.True(item.Month > previousMonth.Value,
                        $"Items in phase {phase.PhaseNumber} are not chronologically ordered");
                }
                previousMonth = item.Month;
            }
        }
    }

    [Fact]
    public async Task PhaseGrouping_WithAwards_CreatesCorrectStructure()
    {
        // Arrange: 6 people, awards after 2 phases (12 months + awards)
        DateTime clubStartDate = new DateTime(2024, 3, 1);
        List<Person> testPeople = CreateTestPeople(6);

        // Create cache with awards event
        Dictionary<DateTime, string> cacheAssignments = new Dictionary<DateTime, string>();
        DateTime currentMonth = clubStartDate.StartOfMonth();

        // Phase 1: 6 months
        for (int i = 0; i < 6; i++)
        {
            cacheAssignments[currentMonth] = testPeople[i].Name!;
            currentMonth = currentMonth.AddMonths(1);
        }

        // Phase 2: 6 months
        for (int i = 0; i < 6; i++)
        {
            cacheAssignments[currentMonth] = testPeople[i].Name!;
            currentMonth = currentMonth.AddMonths(1);
        }

        // Awards Event (March 2025)
        cacheAssignments[currentMonth] = "Awards Event 1";
        currentMonth = currentMonth.AddMonths(1);

        // Phase 3: 6 months
        for (int i = 0; i < 6; i++)
        {
            cacheAssignments[currentMonth] = testPeople[i].Name!;
            currentMonth = currentMonth.AddMonths(1);
        }

        // Setup mocks
        _mockCache
            .Setup(c => c.GetAllAssignmentsAsync())
            .ReturnsAsync(cacheAssignments);

        SetupDefaultMocks(clubStartDate, testPeople, new List<MovieEvent>());

        // Act: Build timeline
        TimelineRenderingService service = CreateService();
        TimelineViewModel timeline = await service.BuildTimelineAsync();

        // Assert: Awards events should be properly separated
        List<TimelinePhase> allPhases = GetAllPhases(timeline);

        // Find the awards phase (should have IsAwardsEvent = true)
        bool foundAwardsPhase = allPhases.Any(phase =>
            phase.Items.Any(item => item.IsAwardsEvent));

        Assert.True(foundAwardsPhase, "Expected to find a phase containing awards event");

        // Verify phase numbers are sequential (no gaps)
        List<int> phaseNumbers = allPhases.Select(p => p.PhaseNumber).OrderBy(n => n).ToList();
        for (int i = 1; i < phaseNumbers.Count; i++)
        {
            int expectedNext = phaseNumbers[i - 1] + 1;
            Assert.Equal(expectedNext, phaseNumbers[i]);
        }

        // Verify phase boundaries are correct (awards create boundaries)
        TimelinePhase? awardsPhase = allPhases.FirstOrDefault(p => p.Items.Any(i => i.IsAwardsEvent));
        if (awardsPhase != null)
        {
            // Awards phase should only contain the awards event
            Assert.Single(awardsPhase.Items);
            Assert.True(awardsPhase.Items[0].IsAwardsEvent);
            Assert.Equal(1, awardsPhase.Items[0].AwardsEventNumber);
        }
    }

    [Fact]
    public async Task PhaseGrouping_FutureMonths_IncludesCacheOnlyData()
    {
        // Arrange: Cache has assignments for 12 future months, but no database records
        DateTime clubStartDate = new DateTime(2024, 3, 1);
        List<Person> testPeople = CreateTestPeople(6);

        // Create cache assignments for future months only
        Dictionary<DateTime, string> cacheAssignments = new Dictionary<DateTime, string>();
        DateTime futureMonth = DateProvider.Now.AddMonths(1).StartOfMonth();

        for (int i = 0; i < 12; i++)
        {
            cacheAssignments[futureMonth] = testPeople[i % 6].Name!;
            futureMonth = futureMonth.AddMonths(1);
        }

        // Setup mocks
        _mockCache
            .Setup(c => c.GetAllAssignmentsAsync())
            .ReturnsAsync(cacheAssignments);

        SetupDefaultMocks(clubStartDate, testPeople, new List<MovieEvent>()); // No existing events

        // Act: Build timeline
        TimelineRenderingService service = CreateService();
        TimelineViewModel timeline = await service.BuildTimelineAsync();

        // Assert: Future phases should contain cache assignments
        Assert.NotEmpty(timeline.FuturePhases);

        // All future phase items should have State == Future
        foreach (TimelinePhase futurePhase in timeline.FuturePhases)
        {
            foreach (TimelineItem item in futurePhase.Items)
            {
                Assert.Equal(TimelineItemState.Future, item.State);

                // Cache-only items should have null MovieEventId
                if (item.State == TimelineItemState.Future && !item.IsAwardsEvent)
                {
                    Assert.Null(item.MovieEventId);
                    Assert.NotNull(item.AssignedPersonName);
                    Assert.NotEmpty(item.AssignedPersonName);
                }
            }
        }

        // Verify total items matches cache count (12 months)
        int totalFutureItems = timeline.FuturePhases.Sum(p => p.Items.Count);
        Assert.Equal(12, totalFutureItems);
    }

    [Fact]
    public async Task PhaseGrouping_MixedDatabaseAndCache_PrioritizesDatabaseRecords()
    {
        // Arrange: Database has records for first 3 months, cache has different assignments
        DateTime clubStartDate = new DateTime(2024, 3, 1);
        List<Person> testPeople = CreateTestPeople(6);

        // Cache says Alice, Bob, Charlie...
        Dictionary<DateTime, string> cacheAssignments = new Dictionary<DateTime, string>();
        DateTime month = clubStartDate.StartOfMonth();
        for (int i = 0; i < 6; i++)
        {
            cacheAssignments[month] = testPeople[i].Name!;
            month = month.AddMonths(1);
        }

        // Database has ACTUAL records (which should take priority)
        List<MovieEvent> existingEvents = new List<MovieEvent>
        {
            new MovieEvent
            {
                Id = Guid.NewGuid(),
                StartDate = clubStartDate,
                EndDate = clubStartDate.AddDays(29),
                Person = "Alice",
                Movie = "Test Movie 1",
                PhaseNumber = 1
            },
            new MovieEvent
            {
                Id = Guid.NewGuid(),
                StartDate = clubStartDate.AddMonths(1),
                EndDate = clubStartDate.AddMonths(1).AddDays(29),
                Person = "Bob",
                Movie = "Test Movie 2",
                PhaseNumber = 1
            }
        };

        // Setup mocks
        _mockCache
            .Setup(c => c.GetAllAssignmentsAsync())
            .ReturnsAsync(cacheAssignments);

        SetupDefaultMocks(clubStartDate, testPeople, existingEvents);

        // Act: Build timeline
        TimelineRenderingService service = CreateService();
        TimelineViewModel timeline = await service.BuildTimelineAsync();

        // Assert: Database records should be enriched with movie data
        List<TimelinePhase> allPhases = GetAllPhases(timeline);

        // Find items that have database records
        List<TimelineItem> itemsWithDb = allPhases
            .SelectMany(p => p.Items)
            .Where(i => i.MovieEventId.HasValue)
            .ToList();

        Assert.Equal(2, itemsWithDb.Count);

        // Verify database enrichment
        TimelineItem firstItem = itemsWithDb.First();
        Assert.NotNull(firstItem.MovieTitle);
        Assert.Equal("Test Movie 1", firstItem.MovieTitle);
        Assert.Equal("Alice", firstItem.AssignedPersonName);

        // Items without database records should have null MovieEventId
        List<TimelineItem> cacheOnlyItems = allPhases
            .SelectMany(p => p.Items)
            .Where(i => !i.MovieEventId.HasValue && !i.IsAwardsEvent)
            .ToList();

        foreach (TimelineItem cacheItem in cacheOnlyItems)
        {
            Assert.Null(cacheItem.MovieEventId);
            Assert.Null(cacheItem.MovieTitle);
            Assert.NotNull(cacheItem.AssignedPersonName);
        }
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
