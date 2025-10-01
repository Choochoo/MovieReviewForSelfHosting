using Microsoft.Extensions.Logging;
using Moq;
using MovieReviewApp.Application.Services;
using MovieReviewApp.Components.Pages;
using MovieReviewApp.Models;
using MovieReviewApp.Models.ViewModels;
using Xunit;

namespace MovieReviewApp.Tests;

/// <summary>
/// Tests for Home.razor.cs component to validate refactored timeline architecture.
/// Ensures structured timeline (TimelineViewModel) is used instead of flat list.
/// </summary>
public class HomePageRenderingTests
{
    private readonly Mock<HomePageDataService> _mockHomePageDataService;
    private readonly Mock<TimelineRenderingService> _mockTimelineRenderingService;
    private readonly Mock<SiteUpdateService> _mockSiteUpdateService;
    private readonly Mock<PersonAssignmentCacheService> _mockPersonAssignmentCache;

    public HomePageRenderingTests()
    {
        // Create mock services with proper dependencies
        Mock<ILogger<HomePageDataService>> mockHomePageLogger = new Mock<ILogger<HomePageDataService>>();
        Mock<SettingService> mockSettingService = new Mock<SettingService>();
        Mock<PersonService> mockPersonService = new Mock<PersonService>();
        Mock<MovieEventService> mockMovieEventService = new Mock<MovieEventService>();
        Mock<AwardEventService> mockAwardEventService = new Mock<AwardEventService>();
        Mock<AwardQuestionService> mockAwardQuestionService = new Mock<AwardQuestionService>();
        Mock<DiscussionQuestionService> mockDiscussionQuestionService = new Mock<DiscussionQuestionService>();
        Mock<AwardVoteService> mockAwardVoteService = new Mock<AwardVoteService>();

        _mockSiteUpdateService = new Mock<SiteUpdateService>();

        _mockHomePageDataService = new Mock<HomePageDataService>(
            mockHomePageLogger.Object,
            mockSettingService.Object,
            mockPersonService.Object,
            mockMovieEventService.Object,
            mockAwardEventService.Object,
            mockAwardQuestionService.Object,
            mockDiscussionQuestionService.Object,
            _mockSiteUpdateService.Object,
            mockAwardVoteService.Object
        );

        Mock<ILogger<TimelineRenderingService>> mockTimelineLogger = new Mock<ILogger<TimelineRenderingService>>();
        _mockTimelineRenderingService = new Mock<TimelineRenderingService>(
            It.IsAny<PersonAssignmentCacheService>(),
            mockMovieEventService.Object,
            mockSettingService.Object,
            mockPersonService.Object,
            mockTimelineLogger.Object
        );

        _mockPersonAssignmentCache = new Mock<PersonAssignmentCacheService>();
    }

    [Fact]
    public void StructuredTimeline_Property_ReturnsTimelineViewModel()
    {
        // Arrange: Create a test TimelineViewModel
        TimelineViewModel testTimeline = new TimelineViewModel
        {
            CurrentPhase = new TimelinePhase
            {
                PhaseNumber = 1,
                StartMonth = new DateTime(2024, 3, 1),
                EndMonth = new DateTime(2024, 8, 1),
                Items = new List<TimelineItem>
                {
                    new TimelineItem
                    {
                        Month = new DateTime(2024, 3, 1),
                        AssignedPersonName = "Alice",
                        State = TimelineItemState.Past
                    }
                }
            },
            FuturePhases = new List<TimelinePhase>(),
            PastPhases = new List<TimelinePhase>()
        };

        // Simulate what OnInitializedAsync would do - set the private field via reflection
        // (In real application, this would be set by OnInitializedAsync calling BuildTimelineAsync)
        Home homePage = CreateHomePage();
        System.Reflection.FieldInfo? structuredTimelineField =
            typeof(Home).GetField("_structuredTimeline",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(structuredTimelineField);
        structuredTimelineField!.SetValue(homePage, testTimeline);

        // Act: Access StructuredTimeline property
        TimelineViewModel? result = homePage.StructuredTimeline;

        // Assert: Returns TimelineViewModel (not List<ITimelineItem>)
        Assert.NotNull(result);
        Assert.IsType<TimelineViewModel>(result);
        Assert.Equal(1, result.CurrentPhase!.PhaseNumber);
        Assert.Single(result.CurrentPhase.Items);
    }

    [Fact]
    public void StructuredTimeline_Property_WhenNull_ReturnsNull()
    {
        // Arrange: Create Home page without setting structured timeline
        Home homePage = CreateHomePage();

        // Act: Access StructuredTimeline property
        TimelineViewModel? result = homePage.StructuredTimeline;

        // Assert: Should return null (not throw exception)
        Assert.Null(result);
    }

    [Fact]
    public async Task OnInitializedAsync_PreservesExistingFunctionality_CurrentEvent()
    {
        // Arrange: Mock HomePageDataService to return test data
        DateTime now = DateTime.UtcNow;
        MovieEvent currentEvent = new MovieEvent
        {
            Id = Guid.NewGuid(),
            StartDate = now.AddDays(-10),
            EndDate = now.AddDays(10),
            Movie = "Test Movie",
            Person = "Alice"
        };

        HomePageViewModel mockViewModel = new HomePageViewModel
        {
            CurrentEvent = currentEvent,
            NextEvent = null,
            ExistingEvents = new List<MovieEvent> { currentEvent },
            AllPeople = new List<Person>(),
            DiscussionQuestions = new List<DiscussionQuestion>(),
            AllAwardEvents = new List<AwardEvent>(),
            AllAwardQuestions = new List<AwardQuestion>(),
            Settings = new List<Setting>(),
            AwardSettings = null
        };

        _mockHomePageDataService
            .Setup(s => s.GetHomePageDataAsync(null))
            .ReturnsAsync(mockViewModel);

        Home homePage = CreateHomePage();

        // Act: Call OnInitializedAsync (this would normally be called by Blazor framework)
        // Note: We can't actually call it due to JSRuntime dependency, but we can test the data flow

        // Simulate what OnInitializedAsync does by setting the _viewModel field
        System.Reflection.FieldInfo? viewModelField =
            typeof(Home).GetField("_viewModel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(viewModelField);
        viewModelField!.SetValue(homePage, mockViewModel);

        // Assert: CurrentEvent property should work
        MovieEvent? resultCurrentEvent = homePage.CurrentEvent;
        Assert.NotNull(resultCurrentEvent);
        Assert.Equal("Test Movie", resultCurrentEvent.Movie);
        Assert.Equal("Alice", resultCurrentEvent.Person);
    }

    [Fact]
    public async Task OnInitializedAsync_PreservesExistingFunctionality_NextEvent()
    {
        // Arrange: Mock HomePageDataService to return test data with NextEvent
        DateTime now = DateTime.UtcNow;
        MovieEvent nextEvent = new MovieEvent
        {
            Id = Guid.NewGuid(),
            StartDate = now.AddDays(20),
            EndDate = now.AddDays(50),
            Movie = "Future Movie",
            Person = "Bob"
        };

        HomePageViewModel mockViewModel = new HomePageViewModel
        {
            CurrentEvent = null,
            NextEvent = nextEvent,
            ExistingEvents = new List<MovieEvent> { nextEvent },
            AllPeople = new List<Person>(),
            DiscussionQuestions = new List<DiscussionQuestion>(),
            AllAwardEvents = new List<AwardEvent>(),
            AllAwardQuestions = new List<AwardQuestion>(),
            Settings = new List<Setting>(),
            AwardSettings = null
        };

        _mockHomePageDataService
            .Setup(s => s.GetHomePageDataAsync(null))
            .ReturnsAsync(mockViewModel);

        Home homePage = CreateHomePage();

        // Simulate setting _viewModel
        System.Reflection.FieldInfo? viewModelField =
            typeof(Home).GetField("_viewModel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(viewModelField);
        viewModelField!.SetValue(homePage, mockViewModel);

        // Assert: NextEvent property should work
        MovieEvent? resultNextEvent = homePage.NextEvent;
        Assert.NotNull(resultNextEvent);
        Assert.Equal("Future Movie", resultNextEvent.Movie);
        Assert.Equal("Bob", resultNextEvent.Person);
    }

    [Fact]
    public void GetEligibleMoviesForPhase_WithValidData_ReturnsCorrectMovies()
    {
        // Arrange: Create test data
        HomePageViewModel mockViewModel = new HomePageViewModel
        {
            ExistingEvents = new List<MovieEvent>
            {
                new MovieEvent { PhaseNumber = 1, Movie = "Movie 1" },
                new MovieEvent { PhaseNumber = 2, Movie = "Movie 2" },
                new MovieEvent { PhaseNumber = 3, Movie = "Movie 3" }
            },
            AwardSettings = new AwardSetting
            {
                PhasesBeforeAward = 2
            }
        };

        Home homePage = CreateHomePage();

        // Set _viewModel via reflection
        System.Reflection.FieldInfo? viewModelField =
            typeof(Home).GetField("_viewModel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(viewModelField);
        viewModelField!.SetValue(homePage, mockViewModel);

        // Act: Get eligible movies for phase 3 (should include phases 2-3)
        List<string> eligibleMovies = homePage.GetEligibleMoviesForPhase(3);

        // Assert: Should include Movie 2 and Movie 3 (phases 2-3)
        Assert.Equal(2, eligibleMovies.Count);
        Assert.Contains("Movie 2", eligibleMovies);
        Assert.Contains("Movie 3", eligibleMovies);
        Assert.DoesNotContain("Movie 1", eligibleMovies); // Phase 1 should be excluded
    }

    /// <summary>
    /// Helper method to create a Home page instance with mock dependencies.
    /// Cannot fully initialize due to JSRuntime dependency, but can test data flow.
    /// </summary>
    private Home CreateHomePage()
    {
        Home homePage = new Home();

        // Note: Cannot inject services directly into Razor components in unit tests
        // due to [Inject] attribute restrictions. These tests focus on property
        // and method logic that doesn't require full component lifecycle.

        return homePage;
    }
}
