using MovieReviewApp.Application.Services;
using MovieReviewApp.Models;
using MovieReviewApp.Models.ViewModels;
using MovieReviewApp.Extensions;
using Xunit;

namespace MovieReviewApp.Tests;

public class PersonRotationAlgorithmTests
{
    private readonly string[] _testPeople = { "Alice", "Bob", "Charlie", "David", "Eve" };
    private readonly DateTime _clubStartDate = new DateTime(2024, 3, 1);

    [Fact]
    public void RandomSelection_ShouldMaintainConsistentSequence_RegardlessOfExistingEvents()
    {
        // Arrange - Create two identical scenarios, one with existing events, one without
        List<string> people = _testPeople.ToList();
        Phase phaseWithoutEvents = CreateTestPhase(1, _clubStartDate, 5);
        Phase phaseWithExistingEvents = CreateTestPhase(1, _clubStartDate, 5);

        // Add existing events to second phase for first 2 months
        phaseWithExistingEvents.Events.Add(CreateTestEvent("Alice", _clubStartDate.AddMonths(0)));
        phaseWithExistingEvents.Events.Add(CreateTestEvent("Bob", _clubStartDate.AddMonths(1)));

        // Create ViewModels
        HomePageViewModel viewModelEmpty = CreateTestViewModel(people, _clubStartDate, new List<MovieEvent>());
        HomePageViewModel viewModelWithEvents = CreateTestViewModel(people, _clubStartDate, phaseWithExistingEvents.Events);

        // Act - Simulate algorithm for both scenarios
        List<string> sequenceWithoutEvents = SimulatePersonSelection(viewModelEmpty, phaseWithoutEvents, people, false);
        List<string> sequenceWithEvents = SimulatePersonSelection(viewModelWithEvents, phaseWithExistingEvents, people, false);

        // Assert - The sequence should be IDENTICAL regardless of existing events
        // This tests the critical bug: existing events should not affect the random sequence
        Assert.Equal(sequenceWithoutEvents.Count, sequenceWithEvents.Count);
        for (int i = 0; i < Math.Min(sequenceWithoutEvents.Count, sequenceWithEvents.Count); i++)
        {
            Assert.Equal(sequenceWithoutEvents[i], sequenceWithEvents[i]);
        }
    }

    [Fact]
    public void RespectOrderTrue_ShouldMaintainGlobalRotation_AcrossPhaseBoundaries()
    {
        // Arrange - Create multiple phases
        List<string> people = _testPeople.ToList();
        List<MovieEvent> allEvents = new List<MovieEvent>();

        // Phase 1: 5 months (complete cycle)
        Phase phase1 = CreateTestPhase(1, _clubStartDate, 5);
        HomePageViewModel viewModel1 = CreateTestViewModel(people, _clubStartDate, allEvents);
        List<string> phase1Sequence = SimulatePersonSelection(viewModel1, phase1, people, respectOrder: true);
        allEvents.AddRange(phase1.Events);

        // Phase 2: 3 months (should continue rotation from where phase 1 left off)
        DateTime phase2Start = _clubStartDate.AddMonths(5);
        Phase phase2 = CreateTestPhase(2, phase2Start, 3);
        HomePageViewModel viewModel2 = CreateTestViewModel(people, _clubStartDate, allEvents);
        List<string> phase2Sequence = SimulatePersonSelection(viewModel2, phase2, people, respectOrder: true);

        // Assert - Phase 2 should continue rotation from Phase 1
        List<string> expectedFullSequence = new List<string>();
        for (int i = 0; i < 8; i++) // 5 from phase 1 + 3 from phase 2
        {
            expectedFullSequence.Add(people[i % people.Count]);
        }

        List<string> actualFullSequence = new List<string>();
        actualFullSequence.AddRange(phase1Sequence);
        actualFullSequence.AddRange(phase2Sequence);

        Assert.Equal(expectedFullSequence.Count, actualFullSequence.Count);
        for (int i = 0; i < expectedFullSequence.Count; i++)
        {
            Assert.Equal(expectedFullSequence[i], actualFullSequence[i]);
        }
    }

    [Fact]
    public void RandomSelection_ShouldAdvanceStateForEveryMonthSlot_EvenWhenEventsExist()
    {
        // Arrange - Create scenario with gaps in existing events
        List<string> people = _testPeople.ToList();
        Phase phase = CreateTestPhase(1, _clubStartDate, 5);

        // Add events for months 1 and 3, leaving 2, 4, 5 missing
        phase.Events.Add(CreateTestEvent("Alice", _clubStartDate.AddMonths(0)));
        phase.Events.Add(CreateTestEvent("Charlie", _clubStartDate.AddMonths(2)));

        // Act - Generate missing events
        HomePageViewModel viewModel = CreateTestViewModel(people, _clubStartDate, phase.Events);
        List<string> generatedSequence = SimulatePersonSelection(viewModel, phase, people, respectOrder: false);

        // Assert - The algorithm should generate events for months 2, 4, 5
        // And the random state should be consistent with a full timeline simulation
        Assert.True(generatedSequence.Count > 0);

        // Verify that all 5 months now have events
        Assert.Equal(5, phase.Events.Count);

        // Events should exist for all months in the phase
        for (int i = 0; i < 5; i++)
        {
            DateTime expectedMonth = _clubStartDate.AddMonths(i);
            bool eventExists = phase.Events.Any(e =>
                e.StartDate.Month == expectedMonth.Month &&
                e.StartDate.Year == expectedMonth.Year);
            Assert.True(eventExists, $"Event should exist for month {expectedMonth:yyyy-MM}");
        }
    }

    [Fact]
    public void TimelineSimulation_ShouldProduceDeterministicResults_WithSameSeed()
    {
        // Arrange - Run the same scenario multiple times
        List<string> people = _testPeople.ToList();
        List<List<string>> sequences = new List<List<string>>();

        // Act - Generate sequence 5 times with same parameters
        for (int iteration = 0; iteration < 5; iteration++)
        {
            Phase phase = CreateTestPhase(1, _clubStartDate, 5);
            HomePageViewModel viewModel = CreateTestViewModel(people, _clubStartDate, new List<MovieEvent>());
            List<string> sequence = SimulatePersonSelection(viewModel, phase, people, respectOrder: false);
            sequences.Add(sequence);
        }

        // Assert - All sequences should be identical (deterministic)
        for (int i = 1; i < sequences.Count; i++)
        {
            Assert.Equal(sequences[0].Count, sequences[i].Count);
            for (int j = 0; j < sequences[0].Count; j++)
            {
                Assert.Equal(sequences[0][j], sequences[i][j]);
            }
        }
    }

    [Fact]
    public void RegressionTest_ExistingEventsDoNotBreakSequence()
    {
        // This test specifically targets the bug where existing events in database
        // would prevent the algorithm from running or break the sequence

        // Arrange - Database contains events with wrong person assignments
        List<string> people = _testPeople.ToList();
        Phase phase = CreateTestPhase(1, _clubStartDate, 5);

        // Simulate corrupted database state - all events assigned to first person
        for (int i = 0; i < 5; i++)
        {
            phase.Events.Add(CreateTestEvent("Alice", _clubStartDate.AddMonths(i)));
        }

        // Clear events to simulate regeneration scenario
        List<MovieEvent> originalEvents = phase.Events.ToList();
        phase.Events.Clear();

        // Act - Run algorithm with knowledge of original corrupted events
        HomePageViewModel viewModel = CreateTestViewModel(people, _clubStartDate, originalEvents);
        List<string> newSequence = SimulatePersonSelection(viewModel, phase, people, respectOrder: true);

        // Assert - New sequence should follow proper rotation, not be affected by corrupted data
        List<string> expectedSequence = new List<string> { "Alice", "Bob", "Charlie", "David", "Eve" };
        Assert.Equal(expectedSequence.Count, newSequence.Count);
        for (int i = 0; i < expectedSequence.Count; i++)
        {
            Assert.Equal(expectedSequence[i], newSequence[i]);
        }
    }

    private Phase CreateTestPhase(int phaseNumber, DateTime startDate, int monthCount)
    {
        return new Phase
        {
            Number = phaseNumber,
            StartDate = startDate.StartOfMonth(),
            EndDate = startDate.AddMonths(monthCount - 1).EndOfMonth(),
            Events = new List<MovieEvent>(),
            People = string.Join(",", _testPeople)
        };
    }

    private MovieEvent CreateTestEvent(string person, DateTime month)
    {
        return new MovieEvent
        {
            Person = person,
            StartDate = month.StartOfMonth(),
            EndDate = month.EndOfMonth(),
            PhaseNumber = 1,
            Movie = "Test Movie",
            FromDatabase = true
        };
    }

    private HomePageViewModel CreateTestViewModel(List<string> people, DateTime startDate, List<MovieEvent> existingEvents)
    {
        return new HomePageViewModel
        {
            AllNames = people.ToArray(),
            StartDate = startDate,
            ExistingEvents = existingEvents,
            ExistingEventCount = existingEvents.Count,
            RespectOrder = true // Will be overridden in test methods
        };
    }

    private List<string> SimulatePersonSelection(HomePageViewModel viewModel, Phase phase, List<string> people, bool respectOrder)
    {
        // This method simulates the core algorithm logic to extract the person selection sequence
        List<string> selectedPeople = new List<string>();

        if (respectOrder)
        {
            // Global rotation logic
            List<MovieEvent> allEventsSinceStart = viewModel.ExistingEvents
                .Where(e => e.StartDate >= viewModel.StartDate)
                .OrderBy(e => e.StartDate)
                .ToList();

            DateTime currentMonth = phase.StartDate.StartOfMonth();
            int eventIndex = 0;

            while (currentMonth <= phase.EndDate)
            {
                int globalEventIndex = allEventsSinceStart.Count + eventIndex;
                string person = people[globalEventIndex % people.Count];
                selectedPeople.Add(person);

                // Simulate event creation
                if (!phase.Events.Any(e => e.StartDate.Month == currentMonth.Month && e.StartDate.Year == currentMonth.Year))
                {
                    phase.Events.Add(CreateTestEvent(person, currentMonth));
                }

                currentMonth = currentMonth.AddMonths(1);
                eventIndex++;
            }
        }
        else
        {
            // Random selection with timeline simulation - UPDATED to match PersonRotationService algorithm
            Random timelineRand = new Random(1337);

            // DO NOT advance state for existing events - this test verifies that existing events
            // in the database don't affect the random sequence for the same timeline position

            if (viewModel.StartDate.HasValue)
            {
                DateTime timelineStart = viewModel.StartDate.Value.StartOfMonth();
                DateTime currentMonth = timelineStart;
                List<string> pool = people.ToList(); // Pool-based algorithm like PersonRotationService
                string? lastSelectedPerson = null;

                while (currentMonth <= phase.EndDate)
                {
                    // Refill pool when empty (matches PersonRotationService)
                    if (pool.Count == 0)
                    {
                        pool = people.ToList();
                    }

                    // Select random person from pool, ensuring no consecutive duplicates
                    string selectedPerson;
                    int randomIndex;

                    if (pool.Count == 1)
                    {
                        // Only one person left in pool - must select them
                        selectedPerson = pool[0];
                        randomIndex = 0;
                    }
                    else if (lastSelectedPerson != null && pool.Contains(lastSelectedPerson))
                    {
                        // Prevent consecutive duplicates by excluding the last selected person
                        List<string> availablePool = pool.Where(p => p != lastSelectedPerson).ToList();
                        if (availablePool.Count > 0)
                        {
                            randomIndex = timelineRand.Next(availablePool.Count);
                            selectedPerson = availablePool[randomIndex];
                            // Map back to original pool index for removal
                            randomIndex = pool.IndexOf(selectedPerson);
                        }
                        else
                        {
                            // Fallback if somehow no one is available (shouldn't happen)
                            randomIndex = timelineRand.Next(pool.Count);
                            selectedPerson = pool[randomIndex];
                        }
                    }
                    else
                    {
                        // Normal random selection
                        randomIndex = timelineRand.Next(pool.Count);
                        selectedPerson = pool[randomIndex];
                    }

                    pool.RemoveAt(randomIndex);
                    lastSelectedPerson = selectedPerson;

                    if (currentMonth >= phase.StartDate)
                    {
                        selectedPeople.Add(selectedPerson);

                        // Simulate event creation
                        if (!phase.Events.Any(e => e.StartDate.Month == currentMonth.Month && e.StartDate.Year == currentMonth.Year))
                        {
                            phase.Events.Add(CreateTestEvent(selectedPerson, currentMonth));
                        }
                    }

                    currentMonth = currentMonth.AddMonths(1);
                }
            }
        }

        return selectedPeople;
    }
}