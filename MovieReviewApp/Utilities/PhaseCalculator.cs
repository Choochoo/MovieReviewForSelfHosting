using MovieReviewApp.Extensions;
using MovieReviewApp.Models;
using MovieReviewApp.Constants;

namespace MovieReviewApp.Utilities;

/// <summary>
/// Static utility for calculating phase boundaries and phase numbers from timeline data.
/// Eliminates need for Phase database lookups by calculating everything from cache.
/// </summary>
public static class PhaseCalculator
{
    /// <summary>
    /// Calculates which phase number a given month belongs to
    /// </summary>
    /// <param name="targetMonth">Month to find phase for</param>
    /// <param name="clubStartDate">Club start date</param>
    /// <param name="peoplePerPhase">Number of people per phase</param>
    /// <param name="awardsSettings">Awards configuration (null if awards disabled)</param>
    /// <returns>Phase number (1-based)</returns>
    public static int CalculatePhaseNumber(
        DateTime targetMonth,
        DateTime clubStartDate,
        int peoplePerPhase,
        AwardSetting? awardsSettings = null)
    {
        DateTime currentMonth = clubStartDate.StartOfMonth();
        DateTime target = targetMonth.StartOfMonth();

        int phaseNumber = 1;
        int eventsInPhase = 0;
        int consecutivePhases = 0;

        while (currentMonth <= target)
        {
            // Check if this is an awards month
            bool isAwardsMonth = awardsSettings?.AwardsEnabled == true &&
                                 awardsSettings.PhasesBeforeAward > 0 &&
                                 consecutivePhases > 0 &&
                                 consecutivePhases % awardsSettings.PhasesBeforeAward == 0;

            if (isAwardsMonth)
            {
                // Awards month is a gap between phases, belongs to just-completed phase
                if (currentMonth == target)
                    return phaseNumber;

                // Don't increment phaseNumber - awards are gaps, not phases
                eventsInPhase = 0;
                consecutivePhases++;
            }
            else
            {
                // Regular event month
                eventsInPhase++;

                if (currentMonth == target)
                    return phaseNumber;

                // Check if we've completed a phase
                if (eventsInPhase >= peoplePerPhase)
                {
                    phaseNumber++;
                    eventsInPhase = 0;
                    consecutivePhases++;
                }
            }

            currentMonth = currentMonth.AddMonths(1);
        }

        return phaseNumber;
    }

    /// <summary>
    /// Calculates the start and end dates for a given phase number
    /// </summary>
    /// <param name="phaseNumber">Phase number to calculate boundaries for (1-based)</param>
    /// <param name="clubStartDate">Club start date</param>
    /// <param name="peoplePerPhase">Number of people per phase</param>
    /// <param name="awardsSettings">Awards configuration (null if awards disabled)</param>
    /// <returns>Tuple of (StartMonth, EndMonth) or null if phase doesn't exist</returns>
    public static (DateTime StartMonth, DateTime EndMonth)? CalculatePhaseBoundaries(
        int phaseNumber,
        DateTime clubStartDate,
        int peoplePerPhase,
        AwardSetting? awardsSettings = null)
    {
        if (phaseNumber < 1)
            return null;

        DateTime currentMonth = clubStartDate.StartOfMonth();
        int currentPhaseNumber = 1;
        int eventsInPhase = 0;
        int consecutivePhases = 0;

        DateTime? phaseStart = null;
        DateTime? phaseEnd = null;

        // Iterate through months to find the target phase
        int maxMonthsToCheck = CacheConstants.WINDOW_MONTHS * 2; // 2x cache window for safety
        int monthsChecked = 0;

        while (monthsChecked < maxMonthsToCheck)
        {
            // Check if this is an awards month
            bool isAwardsMonth = awardsSettings?.AwardsEnabled == true &&
                                 awardsSettings.PhasesBeforeAward > 0 &&
                                 consecutivePhases > 0 &&
                                 consecutivePhases % awardsSettings.PhasesBeforeAward == 0;

            if (currentPhaseNumber == phaseNumber)
            {
                // We're in the target phase
                phaseStart ??= currentMonth;
                phaseEnd = currentMonth;
            }
            else if (currentPhaseNumber > phaseNumber)
            {
                // We've passed the target phase
                break;
            }

            if (isAwardsMonth)
            {
                // Awards month is a gap between phases, belongs to just-completed phase
                if (currentPhaseNumber == phaseNumber)
                {
                    // This awards month is part of the target phase (as a trailing gap)
                    return (phaseStart!.Value, phaseEnd!.Value);
                }

                // Don't increment currentPhaseNumber - awards are gaps, not phases
                eventsInPhase = 0;
                consecutivePhases++;
            }
            else
            {
                // Regular event month
                eventsInPhase++;

                // Check if we've completed a phase
                if (eventsInPhase >= peoplePerPhase)
                {
                    if (currentPhaseNumber == phaseNumber)
                    {
                        // Completed the target phase
                        return (phaseStart!.Value, phaseEnd!.Value);
                    }

                    currentPhaseNumber++;
                    eventsInPhase = 0;
                    consecutivePhases++;
                }
            }

            currentMonth = currentMonth.AddMonths(1);
            monthsChecked++;
        }

        // Return what we found if we're in the target phase
        if (phaseStart.HasValue && phaseEnd.HasValue)
            return (phaseStart.Value, phaseEnd.Value);

        return null;
    }

    /// <summary>
    /// Determines if a given month is an awards month
    /// </summary>
    /// <param name="targetMonth">Month to check</param>
    /// <param name="clubStartDate">Club start date</param>
    /// <param name="peoplePerPhase">Number of people per phase</param>
    /// <param name="awardsSettings">Awards configuration</param>
    /// <returns>True if the month is an awards month</returns>
    public static bool IsAwardsMonth(
        DateTime targetMonth,
        DateTime clubStartDate,
        int peoplePerPhase,
        AwardSetting? awardsSettings)
    {
        if (awardsSettings?.AwardsEnabled != true || awardsSettings.PhasesBeforeAward <= 0)
            return false;

        DateTime currentMonth = clubStartDate.StartOfMonth();
        DateTime target = targetMonth.StartOfMonth();

        int eventsInPhase = 0;
        int consecutivePhases = 0;

        while (currentMonth <= target)
        {
            // Check if this is an awards month
            bool isAwardsMonth = consecutivePhases > 0 &&
                                 consecutivePhases % awardsSettings.PhasesBeforeAward == 0;

            if (currentMonth == target)
                return isAwardsMonth;

            if (isAwardsMonth)
            {
                eventsInPhase = 0;
                consecutivePhases++;
            }
            else
            {
                eventsInPhase++;

                if (eventsInPhase >= peoplePerPhase)
                {
                    eventsInPhase = 0;
                    consecutivePhases++;
                }
            }

            currentMonth = currentMonth.AddMonths(1);
        }

        return false;
    }

    /// <summary>
    /// Calculates how many regular (non-awards) events have occurred in the current phase up to the target month
    /// </summary>
    /// <param name="targetMonth">Month to count up to (inclusive)</param>
    /// <param name="clubStartDate">Club start date</param>
    /// <param name="peoplePerPhase">Number of people per phase</param>
    /// <param name="awardsSettings">Awards configuration</param>
    /// <returns>Number of events in the current phase</returns>
    public static int CountEventsInCurrentPhase(
        DateTime targetMonth,
        DateTime clubStartDate,
        int peoplePerPhase,
        AwardSetting? awardsSettings = null)
    {
        DateTime currentMonth = clubStartDate.StartOfMonth();
        DateTime target = targetMonth.StartOfMonth();

        int eventsInPhase = 0;
        int consecutivePhases = 0;

        while (currentMonth <= target)
        {
            // Check if this is an awards month
            bool isAwardsMonth = awardsSettings?.AwardsEnabled == true &&
                                 awardsSettings.PhasesBeforeAward > 0 &&
                                 consecutivePhases > 0 &&
                                 consecutivePhases % awardsSettings.PhasesBeforeAward == 0;

            if (isAwardsMonth)
            {
                eventsInPhase = 0;
                consecutivePhases++;
            }
            else
            {
                eventsInPhase++;

                if (eventsInPhase >= peoplePerPhase && currentMonth < target)
                {
                    eventsInPhase = 0;
                    consecutivePhases++;
                }
            }

            currentMonth = currentMonth.AddMonths(1);
        }

        return eventsInPhase;
    }
}
