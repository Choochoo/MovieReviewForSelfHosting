using MovieReviewApp.Models;

namespace MovieReviewApp.Utilities;

public static class EventSequenceValidator
{
    public static bool ValidateEventDates(MovieEvent movieEvent)
    {
        (DateTime expectedStart, DateTime expectedEnd) = MovieEventDateCalculator.CalculateMonthBoundaries(movieEvent.StartDate);
        return movieEvent.StartDate == expectedStart && movieEvent.EndDate == expectedEnd;
    }

    public static bool ValidateNoGapsBetweenEvents(List<MovieEvent> events)
    {
        List<MovieEvent> sortedEvents = events.OrderBy(e => e.StartDate).ToList();
        
        for (int i = 1; i < sortedEvents.Count; i++)
        {
            DateTime previousEnd = sortedEvents[i - 1].EndDate;
            DateTime currentStart = sortedEvents[i].StartDate;
            DateTime expectedStart = previousEnd.AddDays(1).Date;
            
            if (currentStart.Date != expectedStart)
                return false;
        }
        return true;
    }

    public static bool ValidateNoOverlapsBetweenEvents(List<MovieEvent> events)
    {
        return events.OrderBy(e => e.StartDate)
            .Zip(events.OrderBy(e => e.StartDate).Skip(1), (current, next) => current.EndDate < next.StartDate)
            .All(noOverlap => noOverlap);
    }
}