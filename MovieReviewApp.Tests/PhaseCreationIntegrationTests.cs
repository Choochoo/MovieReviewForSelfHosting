using MovieReviewApp.Application.Services;
using MovieReviewApp.Infrastructure.Repositories;
using MovieReviewApp.Models;
using MovieReviewApp.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MovieReviewApp.Tests;

/// <summary>
/// Integration tests for Phase auto-creation functionality across services.
/// Tests the interaction between PhaseService, MovieEventService, and PersonAssignmentCacheService.
/// </summary>
[Collection("Sequential")]
public class PhaseCreationIntegrationTests
{
    private readonly Mock<IRepository<Phase>> _mockPhaseRepository;
    private readonly Mock<IRepository<MovieEvent>> _mockMovieEventRepository;
    private readonly Mock<IRepository<Person>> _mockPersonRepository;
    private readonly Mock<IRepository<Setting>> _mockSettingRepository;
    private readonly Mock<ILogger<PhaseService>> _mockPhaseLogger;
    private readonly Mock<ILogger<MovieEventService>> _mockMovieEventLogger;
    private readonly Mock<ILogger<SettingService>> _mockSettingLogger;
    private readonly Mock<ILogger<PersonService>> _mockPersonLogger;
    private readonly Mock<DemoProtectionService> _mockDemoProtection;
    private readonly Mock<PersonAssignmentCacheService> _mockPersonAssignmentCache;

    public PhaseCreationIntegrationTests()
    {
        _mockPhaseRepository = new Mock<IRepository<Phase>>();
        _mockMovieEventRepository = new Mock<IRepository<MovieEvent>>();
        _mockPersonRepository = new Mock<IRepository<Person>>();
        _mockSettingRepository = new Mock<IRepository<Setting>>();
        _mockPhaseLogger = new Mock<ILogger<PhaseService>>();
        _mockMovieEventLogger = new Mock<ILogger<MovieEventService>>();
        _mockSettingLogger = new Mock<ILogger<SettingService>>();
        _mockPersonLogger = new Mock<ILogger<PersonService>>();
        _mockDemoProtection = new Mock<DemoProtectionService>();

        // Mock PersonAssignmentCacheService
        Mock<Infrastructure.Database.MongoDbService> mockMongoDb = new Mock<Infrastructure.Database.MongoDbService>();
        Mock<ILogger<PersonAssignmentCacheService>> mockCacheLogger = new Mock<ILogger<PersonAssignmentCacheService>>();
        Mock<SettingService> mockSettingService = new Mock<SettingService>(_mockSettingRepository.Object, _mockSettingLogger.Object, _mockDemoProtection.Object);
        _mockPersonAssignmentCache = new Mock<PersonAssignmentCacheService>(mockMongoDb.Object, mockCacheLogger.Object, mockSettingService.Object);
    }

    [Fact]
    public async Task MovieEventService_GetOrCreateForMonthAsync_CreatesPhaseBeforeEvent()
    {
        // Arrange - First event in a new phase
        DateTime targetMonth = new DateTime(2024, 3, 1);
        string assignedPerson = "Alice";
        int expectedPhaseNumber = 1;

        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = "2024-03-01" }
        };

        List<Person> people = new List<Person>
        {
            new Person { Name = "Alice", Order = 1 },
            new Person { Name = "Bob", Order = 2 },
            new Person { Name = "Charlie", Order = 3 }
        };

        Phase createdPhase = new Phase
        {
            Id = Guid.NewGuid(),
            Number = 1,
            StartDate = new DateTime(2024, 3, 1),
            EndDate = new DateTime(2024, 5, 31),
            People = "Alice, Bob, Charlie"
        };

        MovieEvent createdEvent = new MovieEvent
        {
            Id = Guid.NewGuid(),
            Person = assignedPerson,
            StartDate = new DateTime(2024, 3, 1),
            EndDate = new DateTime(2024, 3, 31),
            PhaseNumber = expectedPhaseNumber
        };

        // Setup mocks
        _mockMovieEventRepository.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>()))
            .ReturnsAsync(new List<MovieEvent>()); // No existing events

        _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Phase>()); // No existing phases
        _mockSettingRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(people);
        _mockPhaseRepository.Setup(r => r.CreateAsync(It.IsAny<Phase>())).ReturnsAsync(createdPhase);
        _mockMovieEventRepository.Setup(r => r.CreateAsync(It.IsAny<MovieEvent>())).ReturnsAsync(createdEvent);

        _mockPersonAssignmentCache.Setup(c => c.GetPersonForMonthAsync(It.IsAny<DateTime>())).ReturnsAsync(assignedPerson);

        // Create services
        SettingService settingService = new SettingService(_mockSettingRepository.Object, _mockSettingLogger.Object, _mockDemoProtection.Object);
        PersonService personService = new PersonService(_mockPersonRepository.Object, _mockPersonLogger.Object, _mockDemoProtection.Object);
        PhaseService phaseService = new PhaseService(_mockPhaseRepository.Object, _mockPhaseLogger.Object, settingService, personService);
        MovieEventService movieEventService = new MovieEventService(
            _mockMovieEventRepository.Object,
            _mockMovieEventLogger.Object,
            _mockPersonAssignmentCache.Object,
            phaseService);

        // Act
        MovieEvent? result = await movieEventService.GetOrCreateForMonthAsync(targetMonth);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(assignedPerson, result.Person);
        Assert.Equal(expectedPhaseNumber, result.PhaseNumber);

        // Verify Phase was created before MovieEvent
        _mockPhaseRepository.Verify(r => r.CreateAsync(It.Is<Phase>(p =>
            p.Number == expectedPhaseNumber &&
            p.StartDate.Year == 2024 &&
            p.StartDate.Month == 3
        )), Times.Once);

        _mockMovieEventRepository.Verify(r => r.CreateAsync(It.Is<MovieEvent>(e =>
            e.PhaseNumber == expectedPhaseNumber &&
            e.Person == assignedPerson
        )), Times.Once);
    }

    [Fact]
    public async Task MovieEventService_GetOrCreateForMonthAsync_AwardsMonth_ReturnsNull()
    {
        // Arrange - Awards month (no person assigned)
        DateTime targetMonth = new DateTime(2024, 9, 1);
        string assignedPerson = "Awards Event 1";

        _mockMovieEventRepository.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>()))
            .ReturnsAsync(new List<MovieEvent>()); // No existing events

        _mockPersonAssignmentCache.Setup(c => c.GetPersonForMonthAsync(It.IsAny<DateTime>())).ReturnsAsync(assignedPerson);

        // Create minimal services
        SettingService settingService = new SettingService(_mockSettingRepository.Object, _mockSettingLogger.Object, _mockDemoProtection.Object);
        PersonService personService = new PersonService(_mockPersonRepository.Object, _mockPersonLogger.Object, _mockDemoProtection.Object);
        PhaseService phaseService = new PhaseService(_mockPhaseRepository.Object, _mockPhaseLogger.Object, settingService, personService);
        MovieEventService movieEventService = new MovieEventService(
            _mockMovieEventRepository.Object,
            _mockMovieEventLogger.Object,
            _mockPersonAssignmentCache.Object,
            phaseService);

        // Act
        MovieEvent? result = await movieEventService.GetOrCreateForMonthAsync(targetMonth);

        // Assert
        Assert.Null(result); // Awards months return null
        _mockPhaseRepository.Verify(r => r.CreateAsync(It.IsAny<Phase>()), Times.Never);
        _mockMovieEventRepository.Verify(r => r.CreateAsync(It.IsAny<MovieEvent>()), Times.Never);
    }

    [Fact]
    public async Task PhaseService_GetOrCreatePhaseAsync_CalculatesStartDateFromClubStart()
    {
        // Arrange - Test that Phase 2 starts at correct month
        DateTime clubStartDate = new DateTime(2024, 3, 1);
        DateTime targetMonth = new DateTime(2024, 9, 1); // 6 months after start
        int phaseNumber = 2;

        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = "2024-03-01" }
        };

        List<Person> people = new List<Person>
        {
            new Person { Name = "Alice", Order = 1 },
            new Person { Name = "Bob", Order = 2 },
            new Person { Name = "Charlie", Order = 3 },
            new Person { Name = "Diana", Order = 4 },
            new Person { Name = "Eve", Order = 5 },
            new Person { Name = "Frank", Order = 6 }
        };

        Phase createdPhase = new Phase
        {
            Id = Guid.NewGuid(),
            Number = 2,
            StartDate = new DateTime(2024, 9, 1), // Should be clubStartDate + 6 months
            EndDate = new DateTime(2025, 2, 28),  // 6 people = 6 months
            People = "Alice, Bob, Charlie, Diana, Eve, Frank"
        };

        _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Phase>());
        _mockSettingRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(people);
        _mockPhaseRepository.Setup(r => r.CreateAsync(It.IsAny<Phase>())).ReturnsAsync(createdPhase);

        SettingService settingService = new SettingService(_mockSettingRepository.Object, _mockSettingLogger.Object, _mockDemoProtection.Object);
        PersonService personService = new PersonService(_mockPersonRepository.Object, _mockPersonLogger.Object, _mockDemoProtection.Object);
        PhaseService phaseService = new PhaseService(_mockPhaseRepository.Object, _mockPhaseLogger.Object, settingService, personService);

        // Act
        Phase result = await phaseService.GetOrCreatePhaseAsync(targetMonth, phaseNumber);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Number);
        Assert.Equal(new DateTime(2024, 9, 1), result.StartDate);

        // Verify creation with correct calculated start date
        _mockPhaseRepository.Verify(r => r.CreateAsync(It.Is<Phase>(p =>
            p.Number == 2 &&
            p.StartDate.Year == 2024 &&
            p.StartDate.Month == 9
        )), Times.Once);
    }

    [Fact]
    public async Task PhaseService_GetOrCreatePhaseAsync_ThrowsWhenNoStartDate()
    {
        // Arrange - Missing StartDate setting
        DateTime targetMonth = new DateTime(2024, 3, 1);
        int phaseNumber = 1;

        List<Setting> settings = new List<Setting>(); // No StartDate setting

        _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Phase>());
        _mockSettingRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(settings);

        SettingService settingService = new SettingService(_mockSettingRepository.Object, _mockSettingLogger.Object, _mockDemoProtection.Object);
        PersonService personService = new PersonService(_mockPersonRepository.Object, _mockPersonLogger.Object, _mockDemoProtection.Object);
        PhaseService phaseService = new PhaseService(_mockPhaseRepository.Object, _mockPhaseLogger.Object, settingService, personService);

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await phaseService.GetOrCreatePhaseAsync(targetMonth, phaseNumber));

        Assert.Contains("StartDate setting is required", exception.Message);
        _mockPhaseRepository.Verify(r => r.CreateAsync(It.IsAny<Phase>()), Times.Never);
    }

    [Fact]
    public async Task PhaseService_GetOrCreatePhaseAsync_ThrowsWhenNoPeople()
    {
        // Arrange - No people in database
        DateTime targetMonth = new DateTime(2024, 3, 1);
        int phaseNumber = 1;

        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = "2024-03-01" }
        };

        List<Person> people = new List<Person>(); // Empty people list

        _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Phase>());
        _mockSettingRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(people);

        SettingService settingService = new SettingService(_mockSettingRepository.Object, _mockSettingLogger.Object, _mockDemoProtection.Object);
        PersonService personService = new PersonService(_mockPersonRepository.Object, _mockPersonLogger.Object, _mockDemoProtection.Object);
        PhaseService phaseService = new PhaseService(_mockPhaseRepository.Object, _mockPhaseLogger.Object, settingService, personService);

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await phaseService.GetOrCreatePhaseAsync(targetMonth, phaseNumber));

        Assert.Contains("At least one person must exist", exception.Message);
        _mockPhaseRepository.Verify(r => r.CreateAsync(It.IsAny<Phase>()), Times.Never);
    }

    [Fact]
    public async Task PhaseCreation_MultiplePhases_CalculatesCorrectDateRanges()
    {
        // Arrange - Test Phase 1, 2, and 3 with correct boundaries
        DateTime clubStartDate = new DateTime(2024, 3, 1);

        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = "2024-03-01" }
        };

        List<Person> people = new List<Person>
        {
            new Person { Name = "Alice", Order = 1 },
            new Person { Name = "Bob", Order = 2 },
            new Person { Name = "Charlie", Order = 3 }
        };

        // Create test data for 3 phases
        List<(int phaseNumber, DateTime expectedStart, DateTime expectedEnd)> phases = new List<(int, DateTime, DateTime)>
        {
            (1, new DateTime(2024, 3, 1), new DateTime(2024, 5, 31)),   // Phase 1: Mar-May 2024
            (2, new DateTime(2024, 6, 1), new DateTime(2024, 8, 31)),   // Phase 2: Jun-Aug 2024
            (3, new DateTime(2024, 9, 1), new DateTime(2024, 11, 30))   // Phase 3: Sep-Nov 2024
        };

        _mockSettingRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(people);

        SettingService settingService = new SettingService(_mockSettingRepository.Object, _mockSettingLogger.Object, _mockDemoProtection.Object);
        PersonService personService = new PersonService(_mockPersonRepository.Object, _mockPersonLogger.Object, _mockDemoProtection.Object);
        PhaseService phaseService = new PhaseService(_mockPhaseRepository.Object, _mockPhaseLogger.Object, settingService, personService);

        // Act & Assert each phase
        foreach ((int phaseNumber, DateTime expectedStart, DateTime expectedEnd) phaseData in phases)
        {
            Phase testPhase = new Phase
            {
                Id = Guid.NewGuid(),
                Number = phaseData.phaseNumber,
                StartDate = phaseData.expectedStart,
                EndDate = phaseData.expectedEnd,
                People = "Alice, Bob, Charlie"
            };

            _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Phase>()); // No existing phases
            _mockPhaseRepository.Setup(r => r.CreateAsync(It.IsAny<Phase>())).ReturnsAsync(testPhase);

            Phase result = await phaseService.GetOrCreatePhaseAsync(phaseData.expectedStart, phaseData.phaseNumber);

            Assert.Equal(phaseData.phaseNumber, result.Number);
            Assert.Equal(phaseData.expectedStart, result.StartDate);
            Assert.Equal(phaseData.expectedEnd, result.EndDate);
        }
    }

    [Fact]
    public async Task PhaseService_GetOrCreatePhaseAsync_ExistingPhase_DoesNotRecreate()
    {
        // Arrange - Phase already exists
        DateTime targetMonth = new DateTime(2024, 3, 1);
        int phaseNumber = 1;

        Phase existingPhase = new Phase
        {
            Id = Guid.NewGuid(),
            Number = 1,
            StartDate = new DateTime(2024, 3, 1),
            EndDate = new DateTime(2024, 5, 31),
            People = "Alice, Bob, Charlie"
        };

        _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Phase> { existingPhase });

        SettingService settingService = new SettingService(_mockSettingRepository.Object, _mockSettingLogger.Object, _mockDemoProtection.Object);
        PersonService personService = new PersonService(_mockPersonRepository.Object, _mockPersonLogger.Object, _mockDemoProtection.Object);
        PhaseService phaseService = new PhaseService(_mockPhaseRepository.Object, _mockPhaseLogger.Object, settingService, personService);

        // Act
        Phase result = await phaseService.GetOrCreatePhaseAsync(targetMonth, phaseNumber);

        // Assert
        Assert.Equal(existingPhase.Id, result.Id);
        Assert.Equal(existingPhase.Number, result.Number);

        // Should not call settings or people services
        _mockSettingRepository.Verify(r => r.GetAllAsync(), Times.Never);
        _mockPersonRepository.Verify(r => r.GetAllAsync(), Times.Never);
        _mockPhaseRepository.Verify(r => r.CreateAsync(It.IsAny<Phase>()), Times.Never);
    }

    [Fact]
    public async Task MovieEventService_ExistingEvent_SkipsPhaseCreation()
    {
        // Arrange - Event already exists for the month
        DateTime targetMonth = new DateTime(2024, 3, 1);

        MovieEvent existingEvent = new MovieEvent
        {
            Id = Guid.NewGuid(),
            Person = "Alice",
            StartDate = new DateTime(2024, 3, 1),
            EndDate = new DateTime(2024, 3, 31),
            PhaseNumber = 1
        };

        _mockMovieEventRepository.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>()))
            .ReturnsAsync(new List<MovieEvent> { existingEvent });

        SettingService settingService = new SettingService(_mockSettingRepository.Object, _mockSettingLogger.Object, _mockDemoProtection.Object);
        PersonService personService = new PersonService(_mockPersonRepository.Object, _mockPersonLogger.Object, _mockDemoProtection.Object);
        PhaseService phaseService = new PhaseService(_mockPhaseRepository.Object, _mockPhaseLogger.Object, settingService, personService);
        MovieEventService movieEventService = new MovieEventService(
            _mockMovieEventRepository.Object,
            _mockMovieEventLogger.Object,
            _mockPersonAssignmentCache.Object,
            phaseService);

        // Act
        MovieEvent? result = await movieEventService.GetOrCreateForMonthAsync(targetMonth);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(existingEvent.Id, result.Id);

        // Should not check person assignment or create phase
        _mockPersonAssignmentCache.Verify(c => c.GetPersonForMonthAsync(It.IsAny<DateTime>()), Times.Never);
        _mockPhaseRepository.Verify(r => r.GetAllAsync(), Times.Never);
        _mockPhaseRepository.Verify(r => r.CreateAsync(It.IsAny<Phase>()), Times.Never);
    }

    [Fact]
    public async Task PhaseService_PeopleOrderRespected_InPhaseCreation()
    {
        // Arrange - People with mixed order should be sorted correctly
        DateTime targetMonth = new DateTime(2024, 3, 1);
        int phaseNumber = 1;

        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = "2024-03-01" }
        };

        List<Person> people = new List<Person>
        {
            new Person { Name = "Charlie", Order = 3 },
            new Person { Name = "Alice", Order = 1 },
            new Person { Name = "Diana", Order = 4 },
            new Person { Name = "Bob", Order = 2 }
        };

        Phase createdPhase = new Phase
        {
            Id = Guid.NewGuid(),
            Number = 1,
            StartDate = new DateTime(2024, 3, 1),
            EndDate = new DateTime(2024, 6, 30),
            People = "Alice, Bob, Charlie, Diana"
        };

        _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Phase>());
        _mockSettingRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(people);
        _mockPhaseRepository.Setup(r => r.CreateAsync(It.IsAny<Phase>())).ReturnsAsync(createdPhase);

        SettingService settingService = new SettingService(_mockSettingRepository.Object, _mockSettingLogger.Object, _mockDemoProtection.Object);
        PersonService personService = new PersonService(_mockPersonRepository.Object, _mockPersonLogger.Object, _mockDemoProtection.Object);
        PhaseService phaseService = new PhaseService(_mockPhaseRepository.Object, _mockPhaseLogger.Object, settingService, personService);

        // Act
        Phase result = await phaseService.GetOrCreatePhaseAsync(targetMonth, phaseNumber);

        // Assert - People should be ordered by Order field
        Assert.Equal("Alice, Bob, Charlie, Diana", result.People);
        _mockPhaseRepository.Verify(r => r.CreateAsync(It.Is<Phase>(p =>
            p.People == "Alice, Bob, Charlie, Diana"
        )), Times.Once);
    }

    [Theory]
    [InlineData(3, 3)]  // 3 people = 3 months per phase
    [InlineData(6, 6)]  // 6 people = 6 months per phase
    [InlineData(8, 8)]  // 8 people = 8 months per phase
    [InlineData(1, 1)]  // 1 person = 1 month per phase
    public async Task PhaseService_GetOrCreatePhaseAsync_CorrectPhaseDuration(int peopleCount, int expectedMonths)
    {
        // Arrange
        DateTime targetMonth = new DateTime(2024, 3, 1);
        int phaseNumber = 1;

        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = "2024-03-01" }
        };

        List<Person> people = new List<Person>();
        for (int i = 0; i < peopleCount; i++)
        {
            people.Add(new Person { Name = $"Person{i + 1}", Order = i + 1 });
        }

        DateTime expectedEndDate = targetMonth.AddMonths(expectedMonths - 1);

        Phase createdPhase = new Phase
        {
            Id = Guid.NewGuid(),
            Number = 1,
            StartDate = targetMonth,
            EndDate = expectedEndDate,
            People = string.Join(", ", people.Select(p => p.Name))
        };

        _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Phase>());
        _mockSettingRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(people);
        _mockPhaseRepository.Setup(r => r.CreateAsync(It.IsAny<Phase>())).ReturnsAsync(createdPhase);

        SettingService settingService = new SettingService(_mockSettingRepository.Object, _mockSettingLogger.Object, _mockDemoProtection.Object);
        PersonService personService = new PersonService(_mockPersonRepository.Object, _mockPersonLogger.Object, _mockDemoProtection.Object);
        PhaseService phaseService = new PhaseService(_mockPhaseRepository.Object, _mockPhaseLogger.Object, settingService, personService);

        // Act
        Phase result = await phaseService.GetOrCreatePhaseAsync(targetMonth, phaseNumber);

        // Assert
        Assert.Equal(targetMonth, result.StartDate);
        Assert.Equal(expectedEndDate, result.EndDate);

        int monthsDifference = (result.EndDate.Year - result.StartDate.Year) * 12 + result.EndDate.Month - result.StartDate.Month;
        Assert.Equal(expectedMonths - 1, monthsDifference); // -1 because StartDate month is inclusive
    }

    [Fact]
    public async Task PhaseService_CalculatePhaseStartDate_Phase1StartsAtClubStart()
    {
        // Arrange - Phase 1 should always start at club start date
        DateTime clubStartDate = new DateTime(2024, 3, 1);
        DateTime targetMonth = clubStartDate;
        int phaseNumber = 1;

        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = "2024-03-01" }
        };

        List<Person> people = new List<Person>
        {
            new Person { Name = "Alice", Order = 1 },
            new Person { Name = "Bob", Order = 2 },
            new Person { Name = "Charlie", Order = 3 }
        };

        Phase createdPhase = new Phase
        {
            Id = Guid.NewGuid(),
            Number = 1,
            StartDate = clubStartDate,
            EndDate = clubStartDate.AddMonths(2), // 3 people = 3 months - 1
            People = "Alice, Bob, Charlie"
        };

        _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Phase>());
        _mockSettingRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(people);
        _mockPhaseRepository.Setup(r => r.CreateAsync(It.IsAny<Phase>())).ReturnsAsync(createdPhase);

        SettingService settingService = new SettingService(_mockSettingRepository.Object, _mockSettingLogger.Object, _mockDemoProtection.Object);
        PersonService personService = new PersonService(_mockPersonRepository.Object, _mockPersonLogger.Object, _mockDemoProtection.Object);
        PhaseService phaseService = new PhaseService(_mockPhaseRepository.Object, _mockPhaseLogger.Object, settingService, personService);

        // Act
        Phase result = await phaseService.GetOrCreatePhaseAsync(targetMonth, phaseNumber);

        // Assert
        Assert.Equal(clubStartDate, result.StartDate);
        _mockPhaseRepository.Verify(r => r.CreateAsync(It.Is<Phase>(p =>
            p.StartDate == clubStartDate
        )), Times.Once);
    }
}