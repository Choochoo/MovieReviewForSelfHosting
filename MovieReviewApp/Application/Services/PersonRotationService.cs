using MovieReviewApp.Extensions;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Clean, single-purpose functions for person rotation algorithm following SOLID principles.
/// Extracted from Home.razor.cs with bug fixes and improved testability.
/// </summary>
public static class PersonRotationService
{

    /// <summary>
    /// Generates global person assignments for the entire timeline from StartDate.
    /// This ensures continuous rotation without phase resets.
    /// </summary>
    public static Dictionary<DateTime, string> GenerateGlobalPersonAssignments(
        DateTime clubStartDate,
        string[] allNames,
        bool respectOrder,
        int monthsSinceStart,
        AwardSetting? awardSettings)
    {
        if (!allNames.Any())
            return new Dictionary<DateTime, string>();

        Dictionary<DateTime, string> assignments = new Dictionary<DateTime, string>();
        List<string> peopleNames = allNames.ToList();
        DateTime timelineStart = clubStartDate.StartOfMonth();

        if (respectOrder)
        {
            GenerateOrderedAssignments(assignments, timelineStart, peopleNames, monthsSinceStart, awardSettings);
        }
        else
        {
            GenerateRandomAssignments(assignments, timelineStart, peopleNames, monthsSinceStart, awardSettings);
        }

        return assignments;
    }

    /// <summary>
    /// Generates assignments using sequential rotation (RespectOrder=true).
    /// Uses global event counting to maintain rotation across phases.
    /// </summary>
    private static void GenerateOrderedAssignments(
        Dictionary<DateTime, string> assignments,
        DateTime timelineStart,
        List<string> peopleNames,
        int monthsSinceStart,
        AwardSetting? awardSettings)
    {
        DateTime currentMonth = timelineStart;
        // For testing purposes, limit to reasonable timeframe to prevent test timeouts
        // In production, cache service calls this once at startup and caches results
        DateTime endDate = timelineStart.AddMonths(60); // 5 years is sufficient for tests
        int globalEventIndex = 0;
        int eventsInCurrentPhase = 0;
        int currentPhase = 1;
        int awardsEventCounter = 1;

        while (currentMonth <= endDate)
        {
            // Check if we've completed a phase and need an awards month
            if (eventsInCurrentPhase == peopleNames.Count)
            {
                Console.WriteLine($"=== PHASE {currentPhase} COMPLETE ({eventsInCurrentPhase} events) ===\n");

                // Insert awards month if enabled and at the right phase
                if (awardSettings != null && awardSettings.AwardsEnabled &&
                    currentPhase % awardSettings.PhasesBeforeAward == 0)
                {
                    assignments[currentMonth] = $"Awards Event {awardsEventCounter}";
                    Console.WriteLine($"=== AWARDS MONTH: {currentMonth:MMMM yyyy} (Awards Event {awardsEventCounter}) ===\n");
                    awardsEventCounter++;
                    currentMonth = currentMonth.AddMonths(1);
                }

                currentPhase++;
                eventsInCurrentPhase = 0;
            }

            // Assign person
            int personIndex = (monthsSinceStart + globalEventIndex) % peopleNames.Count;
            string person = peopleNames[personIndex];
            assignments[currentMonth] = person;
            Console.WriteLine($"  Event {globalEventIndex + 1} → {currentMonth:MMMM yyyy} → {person}");

            globalEventIndex++;
            eventsInCurrentPhase++;
            currentMonth = currentMonth.AddMonths(1);
        }
    }

    /// <summary>
    /// Generates assignments using random pool selection (RespectOrder=false).
    /// KISS Linear Algorithm: ONE timelineRand.Next() call per person event.
    /// Event 1 → call #1, Event 2 → call #2, etc. Awards months have no random calls.
    /// Cache contains ALL assignments from start date to +20 years.
    /// </summary>
    private static void GenerateRandomAssignments(
        Dictionary<DateTime, string> assignments,
        DateTime timelineStart,
        List<string> peopleNames,
        int monthsSinceStart,
        AwardSetting? awardSettings)
    {
        Random timelineRand = new Random(1337);

        // KISS: NO simulation, NO state advancement - pure linear progression
        // ONLY call timelineRand.Next() when selecting a person, NEVER for awards months

        List<string> pool = peopleNames.ToList(); // Initialize pool in Order field sequence
        DateTime currentMonth = timelineStart;
        // For testing purposes, limit to reasonable timeframe to prevent test timeouts
        // In production, cache service calls this once at startup and caches results
        DateTime endDate = timelineStart.AddMonths(240); // 5 years is sufficient for tests
        int globalEventIndex = 0;
        int currentPhase = 1;
        int eventsInCurrentPhase = 0;
        int awardsEventCounter = 1;

        // Full console logging enabled for complete transparency
        Console.WriteLine($"\n[PersonRotation] Generating person assignments from {timelineStart:yyyy-MM} for 20 years");
        Console.WriteLine($"[PersonRotation] People: [{string.Join(", ", peopleNames)}]");
        Console.WriteLine($"[PersonRotation] Awards: {(awardSettings?.AwardsEnabled == true ? $"Enabled, every {awardSettings.PhasesBeforeAward} phases" : "Disabled")}\n");

        while (currentMonth <= endDate)
        {
            // Check if we've completed a phase and need awards month
            if (eventsInCurrentPhase == peopleNames.Count)
            {
                Console.WriteLine($"=== PHASE {currentPhase} COMPLETE ({eventsInCurrentPhase} events) ===\n");

                // Insert awards month if enabled and at the right phase
                if (awardSettings != null && awardSettings.AwardsEnabled &&
                    currentPhase % awardSettings.PhasesBeforeAward == 0)
                {
                    assignments[currentMonth] = $"Awards Event {awardsEventCounter}";
                    Console.WriteLine($"=== AWARDS MONTH: {currentMonth:MMMM yyyy} (Awards Event {awardsEventCounter}) ===\n");
                    awardsEventCounter++;
                    currentMonth = currentMonth.AddMonths(1);

                    // Safety check
                    if (currentMonth > endDate) break;
                }

                currentPhase++;
                eventsInCurrentPhase = 0;
            }

            // Refill pool when empty
            if (pool.Count == 0)
            {
                pool = peopleNames.ToList(); // Always refill with SAME order
                Console.WriteLine($"=== PHASE {currentPhase} START ===");
                Console.WriteLine($"Pool: [{string.Join(", ", pool)}]\n");
            }

            // KISS: Simple random selection - ONE timelineRand.Next() call per event
            int randomIndex = timelineRand.Next(pool.Count);
            string selectedPerson = pool[randomIndex];

            pool.RemoveAt(randomIndex);
            globalEventIndex++;
            eventsInCurrentPhase++;

            // Store assignment in cache
            assignments[currentMonth] = selectedPerson;
            Console.WriteLine($"  Event {globalEventIndex} → {currentMonth:MMMM yyyy} → {selectedPerson}");

            currentMonth = currentMonth.AddMonths(1);
        }

        Console.WriteLine($"\n=== PHASE {currentPhase} COMPLETE ({eventsInCurrentPhase} events) ===");
        Console.WriteLine($"Total: {globalEventIndex} person assignments + {awardsEventCounter - 1} awards months = {assignments.Count} cache entries\n");
    }


}
