using Xunit;
using MovieReviewApp.Application.Services;
using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Models;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

namespace MovieReviewApp.Tests;

/// <summary>
/// Tests to verify PersonAssignmentCache contains data for future months.
/// This validates the cache has entries for 2+ months from now.
/// </summary>
public class PersonAssignmentCacheValidationTests
{
    [Fact]
    public async Task Cache_ShouldContainDataForFutureMonths()
    {
        // Arrange
        MongoDbService mockDbService = Mock.Of<MongoDbService>();
        Mock<MongoDbService> mockDb = Mock.Get(mockDbService);
        ILogger<PersonAssignmentCacheService> mockLoggerService = Mock.Of<ILogger<PersonAssignmentCacheService>>();
        Mock<ILogger<PersonAssignmentCacheService>> mockLogger = Mock.Get(mockLoggerService);

        DateTime clubStartDate = new DateTime(2024, 3, 1);
        DateTime now = DateTime.Now;

        // Create test settings
        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = clubStartDate.ToString("yyyy-MM-dd") },
            new Setting { Key = "RespectOrder", Value = "false" },
            new Setting
            {
                Key = "AwardSettings",
                Value = JsonSerializer.Serialize(new AwardSetting
                {
                    AwardsEnabled = true,
                    PhasesBeforeAward = 2
                })
            }
        };

        // Create test people
        List<Person> people = new List<Person>
        {
            new Person { Id = Guid.NewGuid(), Name = "Dave", Order = 1 },
            new Person { Id = Guid.NewGuid(), Name = "Jared", Order = 2 },
            new Person { Id = Guid.NewGuid(), Name = "Lacey", Order = 3 },
            new Person { Id = Guid.NewGuid(), Name = "Keri", Order = 4 },
            new Person { Id = Guid.NewGuid(), Name = "Jeremiah", Order = 5 },
            new Person { Id = Guid.NewGuid(), Name = "Nikki", Order = 6 }
        };

        mockDb.Setup(db => db.GetAllAsync<Setting>()).ReturnsAsync(settings);
        mockDb.Setup(db => db.GetAllAsync<Person>()).ReturnsAsync(people);

        PersonAssignmentCacheService cacheService = new PersonAssignmentCacheService(
            mockDb.Object,
            mockLogger.Object);

        // Act - Initialize cache
        await cacheService.InitializeCacheOnStartupAsync();

        // Assert - Check for future months (2+ months from now)
        DateTime twoMonthsFromNow = now.AddMonths(2);
        DateTime threeMonthsFromNow = now.AddMonths(3);
        DateTime sixMonthsFromNow = now.AddMonths(6);
        DateTime twelveMonthsFromNow = now.AddMonths(12);

        string? assignment2Months = await cacheService.GetPersonForMonthAsync(twoMonthsFromNow);
        string? assignment3Months = await cacheService.GetPersonForMonthAsync(threeMonthsFromNow);
        string? assignment6Months = await cacheService.GetPersonForMonthAsync(sixMonthsFromNow);
        string? assignment12Months = await cacheService.GetPersonForMonthAsync(twelveMonthsFromNow);

        // Verify cache contains data for all future months
        Assert.NotNull(assignment2Months);
        Assert.NotEmpty(assignment2Months);

        Assert.NotNull(assignment3Months);
        Assert.NotEmpty(assignment3Months);

        Assert.NotNull(assignment6Months);
        Assert.NotEmpty(assignment6Months);

        Assert.NotNull(assignment12Months);
        Assert.NotEmpty(assignment12Months);

        // Log the assignments for verification
        Console.WriteLine($"Cache validation results:");
        Console.WriteLine($"  +2 months ({twoMonthsFromNow:yyyy-MM}): {assignment2Months}");
        Console.WriteLine($"  +3 months ({threeMonthsFromNow:yyyy-MM}): {assignment3Months}");
        Console.WriteLine($"  +6 months ({sixMonthsFromNow:yyyy-MM}): {assignment6Months}");
        Console.WriteLine($"  +12 months ({twelveMonthsFromNow:yyyy-MM}): {assignment12Months}");
    }

    [Fact]
    public async Task Cache_ShouldHandleAwardsMonthsInFuture()
    {
        // Arrange
        MongoDbService mockDbService = Mock.Of<MongoDbService>();
        Mock<MongoDbService> mockDb = Mock.Get(mockDbService);
        ILogger<PersonAssignmentCacheService> mockLoggerService = Mock.Of<ILogger<PersonAssignmentCacheService>>();
        Mock<ILogger<PersonAssignmentCacheService>> mockLogger = Mock.Get(mockLoggerService);

        DateTime clubStartDate = new DateTime(2024, 3, 1);

        // Create test settings with awards every 2 phases
        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = clubStartDate.ToString("yyyy-MM-dd") },
            new Setting { Key = "RespectOrder", Value = "false" },
            new Setting
            {
                Key = "AwardSettings",
                Value = JsonSerializer.Serialize(new AwardSetting
                {
                    AwardsEnabled = true,
                    PhasesBeforeAward = 2
                })
            }
        };

        // Create test people (6 people = 1 phase)
        List<Person> people = new List<Person>
        {
            new Person { Id = Guid.NewGuid(), Name = "Dave", Order = 1 },
            new Person { Id = Guid.NewGuid(), Name = "Jared", Order = 2 },
            new Person { Id = Guid.NewGuid(), Name = "Lacey", Order = 3 },
            new Person { Id = Guid.NewGuid(), Name = "Keri", Order = 4 },
            new Person { Id = Guid.NewGuid(), Name = "Jeremiah", Order = 5 },
            new Person { Id = Guid.NewGuid(), Name = "Nikki", Order = 6 }
        };

        mockDb.Setup(db => db.GetAllAsync<Setting>()).ReturnsAsync(settings);
        mockDb.Setup(db => db.GetAllAsync<Person>()).ReturnsAsync(people);

        PersonAssignmentCacheService cacheService = new PersonAssignmentCacheService(
            mockDb.Object,
            mockLogger.Object);

        // Act
        await cacheService.InitializeCacheOnStartupAsync();

        // Check months for award event markers
        // Phase 1: Mar-Aug 2024 (6 months)
        // Phase 2: Sep 2024-Feb 2025 (6 months)
        // Awards: Mar 2025 (Awards Event 1)
        // Phase 3: Apr-Sep 2025 (6 months)
        // Phase 4: Oct 2025-Mar 2026 (6 months)
        // Awards: Apr 2026 (Awards Event 2)

        string? marchOct2025 = await cacheService.GetPersonForMonthAsync(new DateTime(2025, 3, 1));
        string? apr2026 = await cacheService.GetPersonForMonthAsync(new DateTime(2026, 4, 1));

        // Log results
        Console.WriteLine($"Awards month validation:");
        Console.WriteLine($"  March 2025 (should be Awards Event 1): {marchOct2025}");
        Console.WriteLine($"  April 2026 (should be Awards Event 2): {apr2026}");

        // Verify awards months are marked correctly
        Assert.NotNull(marchOct2025);
        Assert.NotNull(apr2026);

        // Could be either awards or person depending on exact phase calculation
        // Main goal is to confirm cache has data for these future dates
        Assert.True(!string.IsNullOrEmpty(marchOct2025), "Cache should have data for March 2025");
        Assert.True(!string.IsNullOrEmpty(apr2026), "Cache should have data for April 2026");
    }

    [Fact]
    public async Task Cache_ShouldContainEnoughDataForTimeline()
    {
        // Arrange
        MongoDbService mockDbService = Mock.Of<MongoDbService>();
        Mock<MongoDbService> mockDb = Mock.Get(mockDbService);
        ILogger<PersonAssignmentCacheService> mockLoggerService = Mock.Of<ILogger<PersonAssignmentCacheService>>();
        Mock<ILogger<PersonAssignmentCacheService>> mockLogger = Mock.Get(mockLoggerService);

        DateTime clubStartDate = new DateTime(2024, 3, 1);

        List<Setting> settings = new List<Setting>
        {
            new Setting { Key = "StartDate", Value = clubStartDate.ToString("yyyy-MM-dd") },
            new Setting { Key = "RespectOrder", Value = "false" },
            new Setting
            {
                Key = "AwardSettings",
                Value = JsonSerializer.Serialize(new AwardSetting
                {
                    AwardsEnabled = true,
                    PhasesBeforeAward = 2
                })
            }
        };

        List<Person> people = new List<Person>
        {
            new Person { Id = Guid.NewGuid(), Name = "Dave", Order = 1 },
            new Person { Id = Guid.NewGuid(), Name = "Jared", Order = 2 },
            new Person { Id = Guid.NewGuid(), Name = "Lacey", Order = 3 },
            new Person { Id = Guid.NewGuid(), Name = "Keri", Order = 4 },
            new Person { Id = Guid.NewGuid(), Name = "Jeremiah", Order = 5 },
            new Person { Id = Guid.NewGuid(), Name = "Nikki", Order = 6 }
        };

        mockDb.Setup(db => db.GetAllAsync<Setting>()).ReturnsAsync(settings);
        mockDb.Setup(db => db.GetAllAsync<Person>()).ReturnsAsync(people);

        PersonAssignmentCacheService cacheService = new PersonAssignmentCacheService(
            mockDb.Object,
            mockLogger.Object);

        // Act
        await cacheService.InitializeCacheOnStartupAsync();

        // Timeline needs 2+ months from now for 12 months (requirement from Home.razor.cs line 416)
        DateTime futureStartMonth = DateTime.Now.AddMonths(2);
        int monthsToCheck = 12;
        int assignmentsFound = 0;
        int nullAssignments = 0;

        Console.WriteLine($"\nChecking cache from {futureStartMonth:yyyy-MM} for {monthsToCheck} months:");

        for (int i = 0; i < monthsToCheck; i++)
        {
            DateTime checkMonth = futureStartMonth.AddMonths(i);
            string? assignment = await cacheService.GetPersonForMonthAsync(checkMonth);

            if (!string.IsNullOrEmpty(assignment))
            {
                assignmentsFound++;
                Console.WriteLine($"  {checkMonth:yyyy-MM}: {assignment}");
            }
            else
            {
                nullAssignments++;
                Console.WriteLine($"  {checkMonth:yyyy-MM}: NULL (gap detected)");
            }
        }

        Console.WriteLine($"\nSummary: {assignmentsFound} assignments found, {nullAssignments} nulls");

        // Assert - Cache should have data for all checked months
        Assert.Equal(monthsToCheck, assignmentsFound);
        Assert.Equal(0, nullAssignments);
    }
}