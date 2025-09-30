using MovieReviewApp.Application.Services;
using MovieReviewApp.Infrastructure.Repositories;
using MovieReviewApp.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MovieReviewApp.Tests;

/// <summary>
/// Unit tests for PhaseService to verify phase number calculation and phase creation logic.
/// Tests various scenarios including empty databases, different people counts, and date boundaries.
/// </summary>
[Collection("Sequential")]
public class PhaseServiceTests
{
    private readonly Mock<IRepository<Phase>> _mockPhaseRepository;
    private readonly Mock<ILogger<PhaseService>> _mockLogger;
    private readonly Mock<SettingService> _mockSettingService;
    private readonly Mock<PersonService> _mockPersonService;
    private readonly PhaseService _phaseService;

    public PhaseServiceTests()
    {
        _mockPhaseRepository = new Mock<IRepository<Phase>>();
        _mockLogger = new Mock<ILogger<PhaseService>>();

        // Mock SettingService dependencies
        Mock<IRepository<Setting>> mockSettingRepo = new Mock<IRepository<Setting>>();
        Mock<ILogger<SettingService>> mockSettingLogger = new Mock<ILogger<SettingService>>();
        Mock<DemoProtectionService> mockDemoProtection = new Mock<DemoProtectionService>();
        _mockSettingService = new Mock<SettingService>(mockSettingRepo.Object, mockSettingLogger.Object, mockDemoProtection.Object);

        // Mock PersonService dependencies
        Mock<IRepository<Person>> mockPersonRepo = new Mock<IRepository<Person>>();
        Mock<ILogger<PersonService>> mockPersonLogger = new Mock<ILogger<PersonService>>();
        _mockPersonService = new Mock<PersonService>(mockPersonRepo.Object, mockPersonLogger.Object, mockDemoProtection.Object);

        _phaseService = new PhaseService(
            _mockPhaseRepository.Object,
            _mockLogger.Object,
            _mockSettingService.Object,
            _mockPersonService.Object
        );
    }

    #region CalculatePhaseNumberForMonthAsync Tests

    [Fact]
    public async Task CalculatePhaseNumberForMonthAsync_FirstMonth_ReturnsPhaseOne()
    {
        // Arrange
        DateTime clubStartDate = new DateTime(2024, 3, 1);
        DateTime targetMonth = new DateTime(2024, 3, 1);

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

        _mockSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);

        // Act
        int phaseNumber = await _phaseService.CalculatePhaseNumberForMonthAsync(targetMonth);

        // Assert
        Assert.Equal(1, phaseNumber);
    }

    [Fact]
    public async Task CalculatePhaseNumberForMonthAsync_SecondPhase_ReturnsPhaseTwo()
    {
        // Arrange - 6 people per phase, so phase 2 starts at month 6
        DateTime clubStartDate = new DateTime(2024, 3, 1);
        DateTime targetMonth = new DateTime(2024, 9, 1); // 6 months after start

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

        _mockSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);

        // Act
        int phaseNumber = await _phaseService.CalculatePhaseNumberForMonthAsync(targetMonth);

        // Assert
        Assert.Equal(2, phaseNumber);
    }

    [Fact]
    public async Task CalculatePhaseNumberForMonthAsync_ThirdPhase_ReturnsPhaseThree()
    {
        // Arrange - 6 people per phase, so phase 3 starts at month 12
        DateTime clubStartDate = new DateTime(2024, 3, 1);
        DateTime targetMonth = new DateTime(2025, 3, 1); // 12 months after start

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

        _mockSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);

        // Act
        int phaseNumber = await _phaseService.CalculatePhaseNumberForMonthAsync(targetMonth);

        // Assert
        Assert.Equal(3, phaseNumber);
    }

    [Fact]
    public async Task CalculatePhaseNumberForMonthAsync_ThreePeoplePerPhase_CorrectCalculation()
    {
        // Arrange - Smaller group with 3 people per phase
        DateTime clubStartDate = new DateTime(2024, 1, 1);
        DateTime targetMonth = new DateTime(2024, 7, 1); // 6 months after start

        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = "2024-01-01" }
        };

        List<Person> people = new List<Person>
        {
            new Person { Name = "Alice", Order = 1 },
            new Person { Name = "Bob", Order = 2 },
            new Person { Name = "Charlie", Order = 3 }
        };

        _mockSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);

        // Act
        int phaseNumber = await _phaseService.CalculatePhaseNumberForMonthAsync(targetMonth);

        // Assert - 6 months / 3 people = phase 3
        Assert.Equal(3, phaseNumber);
    }

    [Fact]
    public async Task CalculatePhaseNumberForMonthAsync_EightPeoplePerPhase_CorrectCalculation()
    {
        // Arrange - Larger group with 8 people per phase
        DateTime clubStartDate = new DateTime(2024, 1, 1);
        DateTime targetMonth = new DateTime(2024, 9, 1); // 8 months after start

        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = "2024-01-01" }
        };

        List<Person> people = new List<Person>
        {
            new Person { Name = "Person1", Order = 1 },
            new Person { Name = "Person2", Order = 2 },
            new Person { Name = "Person3", Order = 3 },
            new Person { Name = "Person4", Order = 4 },
            new Person { Name = "Person5", Order = 5 },
            new Person { Name = "Person6", Order = 6 },
            new Person { Name = "Person7", Order = 7 },
            new Person { Name = "Person8", Order = 8 }
        };

        _mockSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);

        // Act
        int phaseNumber = await _phaseService.CalculatePhaseNumberForMonthAsync(targetMonth);

        // Assert - 8 months / 8 people = phase 2
        Assert.Equal(2, phaseNumber);
    }

    [Fact]
    public async Task CalculatePhaseNumberForMonthAsync_NoStartDateSetting_ReturnsPhaseOne()
    {
        // Arrange
        DateTime targetMonth = new DateTime(2024, 6, 1);

        List<Setting> settings = new List<Setting>(); // No StartDate setting

        List<Person> people = new List<Person>
        {
            new Person { Name = "Alice", Order = 1 },
            new Person { Name = "Bob", Order = 2 }
        };

        _mockSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);

        // Act
        int phaseNumber = await _phaseService.CalculatePhaseNumberForMonthAsync(targetMonth);

        // Assert - Default to phase 1 when no start date
        Assert.Equal(1, phaseNumber);
    }

    [Fact]
    public async Task CalculatePhaseNumberForMonthAsync_InvalidStartDateFormat_ReturnsPhaseOne()
    {
        // Arrange
        DateTime targetMonth = new DateTime(2024, 6, 1);

        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = "invalid-date-format" }
        };

        List<Person> people = new List<Person>
        {
            new Person { Name = "Alice", Order = 1 },
            new Person { Name = "Bob", Order = 2 }
        };

        _mockSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);

        // Act
        int phaseNumber = await _phaseService.CalculatePhaseNumberForMonthAsync(targetMonth);

        // Assert - Default to phase 1 when start date is invalid
        Assert.Equal(1, phaseNumber);
    }

    [Fact]
    public async Task CalculatePhaseNumberForMonthAsync_NoPeople_ReturnsPhaseOne()
    {
        // Arrange
        DateTime clubStartDate = new DateTime(2024, 1, 1);
        DateTime targetMonth = new DateTime(2024, 6, 1);

        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = "2024-01-01" }
        };

        List<Person> people = new List<Person>(); // Empty people list

        _mockSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);

        // Act
        int phaseNumber = await _phaseService.CalculatePhaseNumberForMonthAsync(targetMonth);

        // Assert - Default to phase 1 when no people
        Assert.Equal(1, phaseNumber);
    }

    [Fact]
    public async Task CalculatePhaseNumberForMonthAsync_BeforeStartDate_ReturnsPhaseOne()
    {
        // Arrange - Target month is before club start date
        DateTime clubStartDate = new DateTime(2024, 6, 1);
        DateTime targetMonth = new DateTime(2024, 3, 1); // 3 months before start

        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = "2024-06-01" }
        };

        List<Person> people = new List<Person>
        {
            new Person { Name = "Alice", Order = 1 },
            new Person { Name = "Bob", Order = 2 }
        };

        _mockSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);

        // Act
        int phaseNumber = await _phaseService.CalculatePhaseNumberForMonthAsync(targetMonth);

        // Assert - Negative months calculation, but will still return phase 1 due to formula
        Assert.Equal(1, phaseNumber); // (-3 / 2) + 1 = -1 + 1 = 0, but clamped to 1
    }

    [Fact]
    public async Task CalculatePhaseNumberForMonthAsync_MultipleYearsLater_CorrectCalculation()
    {
        // Arrange - Test with a date multiple years in the future
        DateTime clubStartDate = new DateTime(2024, 3, 1);
        DateTime targetMonth = new DateTime(2027, 3, 1); // Exactly 3 years later (36 months)

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

        _mockSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);

        // Act
        int phaseNumber = await _phaseService.CalculatePhaseNumberForMonthAsync(targetMonth);

        // Assert - 36 months / 6 people = 6, so phase 7 (6 + 1)
        Assert.Equal(7, phaseNumber);
    }

    [Theory]
    [InlineData("2024-03-01", "2024-03-01", 6, 1)] // First month of phase 1
    [InlineData("2024-03-01", "2024-08-01", 6, 1)] // Last month of phase 1 (5 months after start)
    [InlineData("2024-03-01", "2024-09-01", 6, 2)] // First month of phase 2
    [InlineData("2024-03-01", "2025-02-01", 6, 2)] // Last month of phase 2 (11 months after start)
    [InlineData("2024-03-01", "2025-03-01", 6, 3)] // First month of phase 3 (12 months after start)
    [InlineData("2024-01-01", "2024-04-01", 3, 2)] // 3 people, month 3
    [InlineData("2024-01-01", "2024-07-01", 3, 3)] // 3 people, month 6
    public async Task CalculatePhaseNumberForMonthAsync_VariousScenarios_CorrectPhaseNumbers(
        string startDateStr, string targetMonthStr, int peopleCount, int expectedPhase)
    {
        // Arrange
        DateTime clubStartDate = DateTime.Parse(startDateStr);
        DateTime targetMonth = DateTime.Parse(targetMonthStr);

        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = startDateStr }
        };

        List<Person> people = new List<Person>();
        for (int i = 0; i < peopleCount; i++)
        {
            people.Add(new Person { Name = $"Person{i + 1}", Order = i + 1 });
        }

        _mockSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);

        // Act
        int phaseNumber = await _phaseService.CalculatePhaseNumberForMonthAsync(targetMonth);

        // Assert
        Assert.Equal(expectedPhase, phaseNumber);
    }

    [Fact]
    public async Task CalculatePhaseNumberForMonthAsync_MidMonthDate_CalculatesBasedOnMonth()
    {
        // Arrange - Test that calculations work regardless of day within month
        DateTime clubStartDate = new DateTime(2024, 3, 15); // Mid-month
        DateTime targetMonth = new DateTime(2024, 9, 22); // Also mid-month

        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = "2024-03-15" }
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

        _mockSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);

        // Act
        int phaseNumber = await _phaseService.CalculatePhaseNumberForMonthAsync(targetMonth);

        // Assert - Should calculate based on month difference (6 months)
        Assert.Equal(2, phaseNumber);
    }

    [Fact]
    public async Task CalculatePhaseNumberForMonthAsync_SinglePerson_EachMonthIsNewPhase()
    {
        // Arrange - Edge case with only 1 person
        DateTime clubStartDate = new DateTime(2024, 1, 1);
        DateTime targetMonth = new DateTime(2024, 5, 1); // 4 months after start

        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = "2024-01-01" }
        };

        List<Person> people = new List<Person>
        {
            new Person { Name = "Alice", Order = 1 }
        };

        _mockSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(settings);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);

        // Act
        int phaseNumber = await _phaseService.CalculatePhaseNumberForMonthAsync(targetMonth);

        // Assert - 4 months / 1 person = phase 5 (4 + 1)
        Assert.Equal(5, phaseNumber);
    }

    #endregion

    #region GetOrCreatePhaseAsync Tests

    [Fact]
    public async Task GetOrCreatePhaseAsync_PhaseAlreadyExists_ReturnsExistingPhase()
    {
        // Arrange
        DateTime targetMonth = new DateTime(2024, 3, 1);
        int phaseNumber = 1;

        Phase existingPhase = new Phase
        {
            Id = Guid.NewGuid(),
            Number = 1,
            StartDate = new DateTime(2024, 3, 1),
            EndDate = new DateTime(2024, 8, 31),
            People = "Alice,Bob,Charlie,Diana,Eve,Frank"
        };

        List<Phase> existingPhases = new List<Phase> { existingPhase };

        _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(existingPhases);

        // Act
        Phase result = await _phaseService.GetOrCreatePhaseAsync(targetMonth, phaseNumber);

        // Assert
        Assert.Equal(existingPhase.Id, result.Id);
        Assert.Equal(1, result.Number);
        Assert.Equal("Alice,Bob,Charlie,Diana,Eve,Frank", result.People);
        _mockPhaseRepository.Verify(r => r.CreateAsync(It.IsAny<Phase>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreatePhaseAsync_PhaseDoesNotExist_CreatesNewPhase()
    {
        // Arrange
        DateTime targetMonth = new DateTime(2024, 3, 1);
        int phaseNumber = 1;

        List<Phase> existingPhases = new List<Phase>(); // No existing phases

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
            Number = 1,
            StartDate = new DateTime(2024, 3, 1),
            EndDate = new DateTime(2024, 8, 31),
            People = "Alice,Bob,Charlie,Diana,Eve,Frank"
        };

        _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(existingPhases);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);
        _mockPhaseRepository.Setup(r => r.CreateAsync(It.IsAny<Phase>())).ReturnsAsync(createdPhase);

        // Act
        Phase result = await _phaseService.GetOrCreatePhaseAsync(targetMonth, phaseNumber);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Number);
        Assert.Equal("Alice,Bob,Charlie,Diana,Eve,Frank", result.People);
        _mockPhaseRepository.Verify(r => r.CreateAsync(It.Is<Phase>(p =>
            p.Number == 1 &&
            p.People == "Alice,Bob,Charlie,Diana,Eve,Frank"
        )), Times.Once);
    }

    [Fact]
    public async Task GetOrCreatePhaseAsync_NoPeople_CreatesPhaseWithUnknown()
    {
        // Arrange
        DateTime targetMonth = new DateTime(2024, 3, 1);
        int phaseNumber = 1;

        List<Phase> existingPhases = new List<Phase>();
        List<Person> people = new List<Person>(); // Empty people list

        Phase createdPhase = new Phase
        {
            Id = Guid.NewGuid(),
            Number = 1,
            StartDate = new DateTime(2024, 3, 1),
            EndDate = new DateTime(2024, 3, 31),
            People = "Unknown"
        };

        _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(existingPhases);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);
        _mockPhaseRepository.Setup(r => r.CreateAsync(It.IsAny<Phase>())).ReturnsAsync(createdPhase);

        // Act
        Phase result = await _phaseService.GetOrCreatePhaseAsync(targetMonth, phaseNumber);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Number);
        Assert.Equal("Unknown", result.People);
        _mockPhaseRepository.Verify(r => r.CreateAsync(It.Is<Phase>(p =>
            p.People == "Unknown"
        )), Times.Once);
    }

    [Fact]
    public async Task GetOrCreatePhaseAsync_ThreePeople_CorrectDateRange()
    {
        // Arrange - Phase should span 3 months
        DateTime targetMonth = new DateTime(2024, 3, 1);
        int phaseNumber = 1;

        List<Phase> existingPhases = new List<Phase>();

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
            EndDate = new DateTime(2024, 5, 31), // 3 months span
            People = "Alice,Bob,Charlie"
        };

        _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(existingPhases);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);
        _mockPhaseRepository.Setup(r => r.CreateAsync(It.IsAny<Phase>())).ReturnsAsync(createdPhase);

        // Act
        Phase result = await _phaseService.GetOrCreatePhaseAsync(targetMonth, phaseNumber);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(new DateTime(2024, 3, 1), result.StartDate);
        Assert.Equal(new DateTime(2024, 5, 31), result.EndDate);
        _mockPhaseRepository.Verify(r => r.CreateAsync(It.Is<Phase>(p =>
            p.StartDate.Year == 2024 &&
            p.StartDate.Month == 3 &&
            p.EndDate.Year == 2024 &&
            p.EndDate.Month == 5
        )), Times.Once);
    }

    [Fact]
    public async Task GetOrCreatePhaseAsync_SixPeople_CorrectDateRange()
    {
        // Arrange - Phase should span 6 months
        DateTime targetMonth = new DateTime(2024, 3, 1);
        int phaseNumber = 1;

        List<Phase> existingPhases = new List<Phase>();

        List<Person> people = new List<Person>
        {
            new Person { Name = "Person1", Order = 1 },
            new Person { Name = "Person2", Order = 2 },
            new Person { Name = "Person3", Order = 3 },
            new Person { Name = "Person4", Order = 4 },
            new Person { Name = "Person5", Order = 5 },
            new Person { Name = "Person6", Order = 6 }
        };

        Phase createdPhase = new Phase
        {
            Id = Guid.NewGuid(),
            Number = 1,
            StartDate = new DateTime(2024, 3, 1),
            EndDate = new DateTime(2024, 8, 31), // 6 months span
            People = "Person1,Person2,Person3,Person4,Person5,Person6"
        };

        _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(existingPhases);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);
        _mockPhaseRepository.Setup(r => r.CreateAsync(It.IsAny<Phase>())).ReturnsAsync(createdPhase);

        // Act
        Phase result = await _phaseService.GetOrCreatePhaseAsync(targetMonth, phaseNumber);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(new DateTime(2024, 3, 1), result.StartDate);
        Assert.Equal(new DateTime(2024, 8, 31), result.EndDate);
    }

    [Fact]
    public async Task GetOrCreatePhaseAsync_PeopleOrderRespected_CorrectSequence()
    {
        // Arrange - Test that people are ordered by Order field
        DateTime targetMonth = new DateTime(2024, 3, 1);
        int phaseNumber = 1;

        List<Phase> existingPhases = new List<Phase>();

        List<Person> people = new List<Person>
        {
            new Person { Name = "Charlie", Order = 3 },
            new Person { Name = "Alice", Order = 1 },
            new Person { Name = "Bob", Order = 2 }
        };

        Phase createdPhase = new Phase
        {
            Id = Guid.NewGuid(),
            Number = 1,
            StartDate = new DateTime(2024, 3, 1),
            EndDate = new DateTime(2024, 5, 31),
            People = "Alice,Bob,Charlie" // Should be ordered by Order field
        };

        _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(existingPhases);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);
        _mockPhaseRepository.Setup(r => r.CreateAsync(It.IsAny<Phase>())).ReturnsAsync(createdPhase);

        // Act
        Phase result = await _phaseService.GetOrCreatePhaseAsync(targetMonth, phaseNumber);

        // Assert
        Assert.Equal("Alice,Bob,Charlie", result.People);
        _mockPhaseRepository.Verify(r => r.CreateAsync(It.Is<Phase>(p =>
            p.People == "Alice,Bob,Charlie"
        )), Times.Once);
    }

    [Fact]
    public async Task GetOrCreatePhaseAsync_MultiplePhases_CorrectPhaseNumberMatching()
    {
        // Arrange - Test that correct phase is returned when multiple exist
        DateTime targetMonth = new DateTime(2024, 9, 1);
        int phaseNumber = 2;

        Phase phase1 = new Phase
        {
            Id = Guid.NewGuid(),
            Number = 1,
            StartDate = new DateTime(2024, 3, 1),
            EndDate = new DateTime(2024, 8, 31),
            People = "Alice,Bob,Charlie"
        };

        Phase phase2 = new Phase
        {
            Id = Guid.NewGuid(),
            Number = 2,
            StartDate = new DateTime(2024, 9, 1),
            EndDate = new DateTime(2025, 2, 28),
            People = "Alice,Bob,Charlie"
        };

        List<Phase> existingPhases = new List<Phase> { phase1, phase2 };

        _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(existingPhases);

        // Act
        Phase result = await _phaseService.GetOrCreatePhaseAsync(targetMonth, phaseNumber);

        // Assert
        Assert.Equal(phase2.Id, result.Id);
        Assert.Equal(2, result.Number);
        _mockPhaseRepository.Verify(r => r.CreateAsync(It.IsAny<Phase>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreatePhaseAsync_SinglePerson_OneMonthPhase()
    {
        // Arrange - Edge case with 1 person = 1 month phase
        DateTime targetMonth = new DateTime(2024, 3, 1);
        int phaseNumber = 1;

        List<Phase> existingPhases = new List<Phase>();

        List<Person> people = new List<Person>
        {
            new Person { Name = "Alice", Order = 1 }
        };

        Phase createdPhase = new Phase
        {
            Id = Guid.NewGuid(),
            Number = 1,
            StartDate = new DateTime(2024, 3, 1),
            EndDate = new DateTime(2024, 3, 31), // Same month
            People = "Alice"
        };

        _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(existingPhases);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);
        _mockPhaseRepository.Setup(r => r.CreateAsync(It.IsAny<Phase>())).ReturnsAsync(createdPhase);

        // Act
        Phase result = await _phaseService.GetOrCreatePhaseAsync(targetMonth, phaseNumber);

        // Assert
        Assert.Equal(new DateTime(2024, 3, 1), result.StartDate);
        Assert.Equal(new DateTime(2024, 3, 31), result.EndDate);
        Assert.Equal("Alice", result.People);
    }

    [Fact]
    public async Task GetOrCreatePhaseAsync_LogsInformationOnCreation()
    {
        // Arrange
        DateTime targetMonth = new DateTime(2024, 3, 1);
        int phaseNumber = 1;

        List<Phase> existingPhases = new List<Phase>();

        List<Person> people = new List<Person>
        {
            new Person { Name = "Alice", Order = 1 }
        };

        Phase createdPhase = new Phase
        {
            Id = Guid.NewGuid(),
            Number = 1,
            StartDate = new DateTime(2024, 3, 1),
            EndDate = new DateTime(2024, 3, 31),
            People = "Alice"
        };

        _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(existingPhases);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);
        _mockPhaseRepository.Setup(r => r.CreateAsync(It.IsAny<Phase>())).ReturnsAsync(createdPhase);

        // Act
        Phase result = await _phaseService.GetOrCreatePhaseAsync(targetMonth, phaseNumber);

        // Assert
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Auto-created Phase")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOrCreatePhaseAsync_LogsWarningWhenNoPeople()
    {
        // Arrange
        DateTime targetMonth = new DateTime(2024, 3, 1);
        int phaseNumber = 1;

        List<Phase> existingPhases = new List<Phase>();
        List<Person> people = new List<Person>(); // Empty

        Phase createdPhase = new Phase
        {
            Id = Guid.NewGuid(),
            Number = 1,
            StartDate = new DateTime(2024, 3, 1),
            EndDate = new DateTime(2024, 3, 31),
            People = "Unknown"
        };

        _mockPhaseRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(existingPhases);
        _mockPersonService.Setup(p => p.GetAllAsync()).ReturnsAsync(people);
        _mockPhaseRepository.Setup(r => r.CreateAsync(It.IsAny<Phase>())).ReturnsAsync(createdPhase);

        // Act
        Phase result = await _phaseService.GetOrCreatePhaseAsync(targetMonth, phaseNumber);

        // Assert
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("No people found")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}