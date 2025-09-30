using MovieReviewApp.Application.Services;
using Xunit;

namespace MovieReviewApp.Tests;

/// <summary>
/// Tests to validate that the consecutive selection bug is fixed
/// </summary>
public class ConsecutiveSelectionValidationTests
{
    private readonly string[] _testPeople = { "Alice", "Bob", "Charlie", "Dave", "Eve" };
    private readonly DateTime _clubStartDate = new DateTime(2024, 3, 1);

    [Fact]
    public void PersonRotationService_RandomMode_ShouldNeverSelectConsecutiveDuplicates()
    {
        // Test various scenarios with different months since start
        for (int monthsSinceStart = 0; monthsSinceStart < 20; monthsSinceStart++)
        {
            // Act
            Dictionary<DateTime, string> assignments = PersonRotationService.GenerateGlobalPersonAssignments(
                _clubStartDate, _testPeople, respectOrder: false, monthsSinceStart, awardSettings: null);

            // Get first 10 months to check for consecutive selections
            List<KeyValuePair<DateTime, string>> sortedAssignments = assignments
                .OrderBy(kvp => kvp.Key)
                .Take(10)
                .ToList();

            // Assert - No consecutive duplicate selections
            for (int i = 1; i < sortedAssignments.Count; i++)
            {
                string previousPerson = sortedAssignments[i - 1].Value;
                string currentPerson = sortedAssignments[i].Value;

                Assert.True(previousPerson != currentPerson,
                    $"Consecutive selection detected with {monthsSinceStart} months since start: " +
                    $"{previousPerson} selected in both {sortedAssignments[i-1].Key:yyyy-MM} and {sortedAssignments[i].Key:yyyy-MM}");
            }
        }
    }

    [Fact]
    public void PersonRotationService_RandomMode_ShouldBeDeterministic()
    {
        // Act - Generate same scenario multiple times
        List<Dictionary<DateTime, string>> results = new List<Dictionary<DateTime, string>>();

        for (int run = 0; run < 5; run++)
        {
            Dictionary<DateTime, string> assignments = PersonRotationService.GenerateGlobalPersonAssignments(
                _clubStartDate, _testPeople, respectOrder: false, monthsSinceStart: 10, awardSettings: null);
            results.Add(assignments);
        }

        // Assert - All runs should produce identical results
        Dictionary<DateTime, string> firstResult = results[0];
        for (int i = 1; i < results.Count; i++)
        {
            Dictionary<DateTime, string> currentResult = results[i];

            Assert.Equal(firstResult.Count, currentResult.Count);

            foreach (KeyValuePair<DateTime, string> kvp in firstResult)
            {
                Assert.True(currentResult.ContainsKey(kvp.Key),
                    $"Month {kvp.Key:yyyy-MM} missing in run {i}");
                Assert.True(kvp.Value == currentResult[kvp.Key],
                    $"Different person selected for {kvp.Key:yyyy-MM} in run {i}");
            }
        }
    }

    [Fact]
    public void PersonRotationService_OrderedMode_ShouldFollowCorrectSequence()
    {
        // Act
        Dictionary<DateTime, string> assignments = PersonRotationService.GenerateGlobalPersonAssignments(
            _clubStartDate, _testPeople, respectOrder: true, monthsSinceStart: 5, awardSettings: null);

        // Get first 10 months to check sequence
        List<KeyValuePair<DateTime, string>> sortedAssignments = assignments
            .OrderBy(kvp => kvp.Key)
            .Take(10)
            .ToList();

        // Assert - Should follow rotation starting after 5 months since start
        for (int i = 0; i < sortedAssignments.Count; i++)
        {
            int globalEventIndex = 5 + i; // 5 months since start + current index
            string expectedPerson = _testPeople[globalEventIndex % _testPeople.Length];
            string actualPerson = sortedAssignments[i].Value;

            Assert.True(expectedPerson == actualPerson,
                $"Month {sortedAssignments[i].Key:yyyy-MM}: Expected {expectedPerson}, got {actualPerson}");
        }
    }

    [Fact]
    public void PersonRotationService_RandomMode_ShouldRespectPoolManagement()
    {
        // Act
        Dictionary<DateTime, string> assignments = PersonRotationService.GenerateGlobalPersonAssignments(
            _clubStartDate, _testPeople, respectOrder: false, monthsSinceStart: 0, awardSettings: null);

        // Get assignments for first complete cycle (5 people = 5 months)
        List<string> firstCycle = assignments
            .OrderBy(kvp => kvp.Key)
            .Take(_testPeople.Length)
            .Select(kvp => kvp.Value)
            .ToList();

        // Assert - All people should be selected exactly once in first cycle
        foreach (string person in _testPeople)
        {
            int count = firstCycle.Count(p => p == person);
            Assert.True(count == 1,
                $"Person {person} should appear exactly once in first cycle, but appeared {count} times");
        }

        // Assert - All people from test array should be represented
        foreach (string person in _testPeople)
        {
            Assert.True(firstCycle.Contains(person),
                $"Person {person} should appear in the first cycle");
        }
    }
}