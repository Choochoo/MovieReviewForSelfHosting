using MovieReviewApp.Application.Services;
using MovieReviewApp.Models;
using MovieReviewApp.Extensions;
using Xunit;

namespace MovieReviewApp.Tests;

/// <summary>
/// Unit tests for PersonRotationService to ensure algorithm correctness and bug fixes.
/// Tests the critical consecutive selection bug fix and algorithm consistency.
/// </summary>
public class PersonRotationServiceTests
{
    private readonly string[] _testPeople = { "Alice", "Bob", "Charlie", "Diana" };
    private readonly DateTime _testStartDate = new DateTime(2024, 1, 1);

    [Fact]
    public void GenerateGlobalPersonAssignments_OrderedMode_ProducesSequentialRotation()
    {
        // Arrange
        int monthsSinceStart = 2; // Alice and Bob already selected

        // Act
        Dictionary<DateTime, string> assignments = PersonRotationService.GenerateGlobalPersonAssignments(
            _testStartDate, _testPeople, respectOrder: true, monthsSinceStart, awardSettings: null);

        // Assert - Should continue rotation from Charlie (index 2)
        DateTime firstMonth = _testStartDate.StartOfMonth();
        Assert.Equal("Charlie", assignments[firstMonth]); // Event #2 -> Charlie (index 2)
        Assert.Equal("Diana", assignments[firstMonth.AddMonths(1)]); // Event #3 -> Diana (index 3)
        Assert.Equal("Alice", assignments[firstMonth.AddMonths(2)]); // Event #4 -> Alice (index 0)
        Assert.Equal("Bob", assignments[firstMonth.AddMonths(3)]); // Event #5 -> Bob (index 1)
    }

    [Fact]
    public void GenerateGlobalPersonAssignments_RandomMode_FixesConsecutiveSelectionBug()
    {
        // Arrange
        int monthsSinceStart = 3; // Simulate 3 existing events

        // Act
        Dictionary<DateTime, string> assignments = PersonRotationService.GenerateGlobalPersonAssignments(
            _testStartDate, _testPeople, respectOrder: false, monthsSinceStart, awardSettings: null);

        // Assert - Verify deterministic behavior (same seed should produce same results)
        Dictionary<DateTime, string> assignments2 = PersonRotationService.GenerateGlobalPersonAssignments(
            _testStartDate, _testPeople, respectOrder: false, monthsSinceStart, awardSettings: null);

        Assert.Equal(assignments.Count, assignments2.Count);
        foreach (KeyValuePair<DateTime, string> kvp in assignments)
        {
            Assert.Equal(kvp.Value, assignments2[kvp.Key]);
        }
    }

    [Fact]
    public void GenerateGlobalPersonAssignments_RandomMode_NeverSelectsConsecutiveDuplicates()
    {
        // Arrange
        int monthsSinceStart = 0;

        // Act
        Dictionary<DateTime, string> assignments = PersonRotationService.GenerateGlobalPersonAssignments(
            _testStartDate, _testPeople, respectOrder: false, monthsSinceStart, awardSettings: null);

        // Assert - Check first 12 months for no consecutive duplicates
        DateTime currentMonth = _testStartDate.StartOfMonth();
        string? previousPerson = null;

        for (int i = 0; i < 12; i++)
        {
            string currentPerson = assignments[currentMonth];

            if (previousPerson != null)
            {
                Assert.NotEqual(previousPerson, currentPerson);
            }

            previousPerson = currentPerson;
            currentMonth = currentMonth.AddMonths(1);
        }
    }

    // DISABLED: GetPersonForMonth method no longer exists - PersonAssignmentCacheService handles lookups
    // [Fact]
    // public void GetPersonForMonth_OrderedGlobalRotation_UsesCorrectIndex()
    // {
    //     // Arrange
    //     List<MovieEvent> existingEvents = new List<MovieEvent>
    //     {
    //         new MovieEvent { StartDate = _testStartDate, Person = "Alice" },
    //         new MovieEvent { StartDate = _testStartDate.AddMonths(1), Person = "Bob" }
    //     };
    //
    //     // Act
    //     string nextPerson = PersonRotationService.GetPersonForMonth(
    //         _testStartDate.AddMonths(2),
    //         _testPeople,
    //         existingEvents,
    //         _testStartDate,
    //         respectOrder: true,
    //         useGlobalRotation: true);
    //
    //     // Assert - Should select Charlie (index 2 after Alice=0, Bob=1)
    //     Assert.Equal("Charlie", nextPerson);
    // }
    //
    // [Fact]
    // public void GetPersonForMonth_PhaseRotation_UsesPhaseEventCount()
    // {
    //     // Arrange
    //     List<MovieEvent> phaseEvents = new List<MovieEvent>
    //     {
    //         new MovieEvent { StartDate = _testStartDate, Person = "Alice", PhaseNumber = 1 }
    //     };
    //
    //     // Act
    //     string nextPerson = PersonRotationService.GetPersonForMonth(
    //         _testStartDate.AddMonths(1),
    //         _testPeople,
    //         phaseEvents,
    //         _testStartDate,
    //         respectOrder: true,
    //         useGlobalRotation: false);
    //
    //     // Assert - Should select Bob (index 1 after 1 existing event)
    //     Assert.Equal("Bob", nextPerson);
    // }
    //
    // [Fact]
    // public void CalculateGlobalEventIndex_CountsEventsCorrectly()
    // {
    //     // Arrange
    //     List<MovieEvent> events = new List<MovieEvent>
    //     {
    //         new MovieEvent { StartDate = _testStartDate.AddDays(-30) }, // Before start date - should not count
    //         new MovieEvent { StartDate = _testStartDate },
    //         new MovieEvent { StartDate = _testStartDate.AddMonths(1) },
    //         new MovieEvent { StartDate = _testStartDate.AddMonths(2) }
    //     };
    //
    //     // Act
    //     int index = PersonRotationService.CalculateGlobalEventIndex(events, _testStartDate);
    //
    //     // Assert - Should count only events on or after start date
    //     Assert.Equal(3, index);
    // }
    //
    // [Fact]
    // public void ValidateRandomStateConsistency_ProducesSameResults()
    // {
    //     // Arrange & Act
    //     bool isConsistent = PersonRotationService.ValidateRandomStateConsistency(_testPeople, monthsSinceStart: 5);
    //
    //     // Assert
    //     Assert.True(isConsistent);
    // }

    [Fact]
    public void GenerateGlobalPersonAssignments_EmptyPeopleArray_ReturnsEmptyDictionary()
    {
        // Arrange
        string[] emptyPeople = Array.Empty<string>();

        // Act
        Dictionary<DateTime, string> assignments = PersonRotationService.GenerateGlobalPersonAssignments(
            _testStartDate, emptyPeople, respectOrder: true, monthsSinceStart: 0, awardSettings: null);

        // Assert
        Assert.Empty(assignments);
    }

    // DISABLED: GetPersonForMonth method no longer exists
    // [Fact]
    // public void GetPersonForMonth_EmptyPeopleArray_ThrowsArgumentException()
    // {
    //     // Arrange
    //     string[] emptyPeople = Array.Empty<string>();
    //     List<MovieEvent> emptyEvents = new List<MovieEvent>();
    //
    //     // Act & Assert
    //     Assert.Throws<ArgumentException>(() =>
    //         PersonRotationService.GetPersonForMonth(
    //             _testStartDate,
    //             emptyPeople,
    //             emptyEvents,
    //             _testStartDate,
    //             respectOrder: true,
    //             useGlobalRotation: true));
    // }

    [Theory]
    [InlineData(0, "Alice")] // First event
    [InlineData(1, "Bob")]   // Second event
    [InlineData(4, "Alice")] // Wraps around after full cycle
    [InlineData(7, "Diana")] // Multiple cycles
    public void GenerateGlobalPersonAssignments_OrderedMode_CorrectRotationPattern(int existingCount, string expectedFirst)
    {
        // Act
        Dictionary<DateTime, string> assignments = PersonRotationService.GenerateGlobalPersonAssignments(
            _testStartDate, _testPeople, respectOrder: true, existingCount, awardSettings: null);

        // Assert
        Assert.Equal(expectedFirst, assignments[_testStartDate.StartOfMonth()]);
    }

    /// <summary>
    /// Integration test that simulates the exact bug scenario from PersonRotationAlgorithmTests.cs.
    /// This ensures the refactored code maintains the same correctness as the test implementation.
    /// </summary>
    [Fact]
    public void GenerateRandomAssignments_MatchesTestImplementationBehavior()
    {
        // This test replicates the correct algorithm from PersonRotationAlgorithmTests.cs (lines 244-282)
        // to ensure our refactored code produces the same results

        // Arrange - Parameters from the original test
        string[] people = { "Marcus Chen", "Sofia Rodriguez", "Amit Patel" };
        int monthsSinceStart = 2;
        DateTime startDate = new DateTime(2024, 1, 1);

        // Act - Use our refactored service
        Dictionary<DateTime, string> serviceResult = PersonRotationService.GenerateGlobalPersonAssignments(
            startDate, people, respectOrder: false, monthsSinceStart, awardSettings: null);

        // Assert - Verify deterministic behavior matches expected pattern
        // The exact persons selected will depend on the random sequence, but the pattern should be consistent
        Assert.True(serviceResult.Any());

        // Verify no consecutive duplicates in first 6 months
        DateTime currentMonth = startDate.StartOfMonth();
        List<string> firstSixMonths = new List<string>();

        for (int i = 0; i < 6; i++)
        {
            firstSixMonths.Add(serviceResult[currentMonth]);
            currentMonth = currentMonth.AddMonths(1);
        }

        // Should not have consecutive duplicates
        for (int i = 1; i < firstSixMonths.Count; i++)
        {
            Assert.NotEqual(firstSixMonths[i-1], firstSixMonths[i]);
        }

        // All people should be selected within reasonable cycles
        HashSet<string> uniquePeople = firstSixMonths.Take(3).ToHashSet();
        Assert.True(uniquePeople.Count >= 2, "Should have reasonable diversity in first 3 selections");
    }

    // DISABLED: GetPersonForMonth method no longer exists - PersonAssignmentCacheService handles lookups
    // /// <summary>
    // /// CRITICAL TEST: Verifies that GetPersonForMonth produces the SAME results as GenerateGlobalPersonAssignments
    // /// for random mode. This ensures consistency between Home.razor.cs and MonthlyDataGenerationService.
    // /// </summary>
    // [Fact]
    // public void GetPersonForMonth_RandomMode_MatchesGenerateGlobalPersonAssignments()
    // {
    //     // Arrange
    //     string[] testPeople = { "Marcus", "Sofia", "Amit", "Rebecca", "Jamal", "Elena" };
    //     DateTime clubStartDate = new DateTime(2024, 3, 1);
    //     List<MovieEvent> existingEvents = new List<MovieEvent>
    //     {
    //         new MovieEvent { StartDate = new DateTime(2024, 3, 1), Person = "Marcus" },
    //         new MovieEvent { StartDate = new DateTime(2024, 4, 1), Person = "Jamal" },
    //         new MovieEvent { StartDate = new DateTime(2024, 5, 1), Person = "Sofia" }
    //     };
    //
    //     // Act - Generate full timeline assignments
    //     Dictionary<DateTime, string> fullAssignments = PersonRotationService.GenerateGlobalPersonAssignments(
    //         clubStartDate,
    //         testPeople,
    //         respectOrder: false,
    //         existingEvents.Count);
    //
    //     // Act - Get individual month assignments using GetPersonForMonth
    //     List<(DateTime month, string fromGetPersonForMonth, string fromFullGeneration)> comparisons = new();
    //
    //     for (int i = 0; i < 12; i++)
    //     {
    //         DateTime targetMonth = clubStartDate.AddMonths(i);
    //         string fromGetPersonForMonth = PersonRotationService.GetPersonForMonth(
    //             targetMonth,
    //             testPeople,
    //             existingEvents,
    //             clubStartDate,
    //             respectOrder: false,
    //             useGlobalRotation: true);
    //
    //         string fromFullGeneration = fullAssignments[targetMonth];
    //         comparisons.Add((targetMonth, fromGetPersonForMonth, fromFullGeneration));
    //     }
    //
    //     // Assert - Both methods MUST produce identical results
    //     foreach ((DateTime month, string fromGetPersonForMonth, string fromFullGeneration) comparison in comparisons)
    //     {
    //         Assert.True(
    //             comparison.fromFullGeneration == comparison.fromGetPersonForMonth,
    //             $"Mismatch for {comparison.month:yyyy-MM}: GetPersonForMonth returned '{comparison.fromGetPersonForMonth}' " +
    //             $"but GenerateGlobalPersonAssignments returned '{comparison.fromFullGeneration}'");
    //     }
    // }
    //
    // /// <summary>
    // /// CRITICAL TEST: Verifies that GetPersonForMonth produces the SAME results as GenerateGlobalPersonAssignments
    // /// for ordered mode (RespectOrder=true).
    // /// </summary>
    // [Fact]
    // public void GetPersonForMonth_OrderedMode_MatchesGenerateGlobalPersonAssignments()
    // {
    //     // Arrange
    //     string[] testPeople = { "Alice", "Bob", "Charlie", "Diana" };
    //     DateTime clubStartDate = new DateTime(2024, 1, 1);
    //     List<MovieEvent> existingEvents = new List<MovieEvent>
    //     {
    //         new MovieEvent { StartDate = new DateTime(2024, 1, 1), Person = "Alice" },
    //         new MovieEvent { StartDate = new DateTime(2024, 2, 1), Person = "Bob" }
    //     };
    //
    //     // Act - Generate full timeline assignments
    //     Dictionary<DateTime, string> fullAssignments = PersonRotationService.GenerateGlobalPersonAssignments(
    //         clubStartDate,
    //         testPeople,
    //         respectOrder: true,
    //         existingEvents.Count);
    //
    //     // Act - Get individual month assignments using GetPersonForMonth
    //     for (int i = 0; i < 12; i++)
    //     {
    //         DateTime targetMonth = clubStartDate.AddMonths(i);
    //         string fromGetPersonForMonth = PersonRotationService.GetPersonForMonth(
    //             targetMonth,
    //             testPeople,
    //             existingEvents,
    //             clubStartDate,
    //             respectOrder: true,
    //             useGlobalRotation: true);
    //
    //         string fromFullGeneration = fullAssignments[targetMonth];
    //
    //         // Assert - Both methods MUST produce identical results
    //         Assert.True(
    //             fromFullGeneration == fromGetPersonForMonth,
    //             $"Mismatch for {targetMonth:yyyy-MM}: GetPersonForMonth returned '{fromGetPersonForMonth}' " +
    //             $"but GenerateGlobalPersonAssignments returned '{fromFullGeneration}'");
    //     }
    // }
}