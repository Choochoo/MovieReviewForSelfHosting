using MovieReviewApp.Models;
using MovieReviewApp.Utilities;
using MovieReviewApp.Extensions;

namespace MovieReviewApp.Application.Services;

public class PhaseEventGenerator
{
    public static MovieEvent CreateMovieEvent(string person, DateTime month, int phaseNumber)
    {
        (DateTime startDate, DateTime endDate) = MovieEventDateCalculator.CalculateMonthBoundaries(month);
        
        return new MovieEvent
        {
            StartDate = startDate,
            EndDate = endDate,
            Person = person,
            PhaseNumber = phaseNumber,
            FromDatabase = false,
            IsEditing = false,
            MeetupTime = DateTime.SpecifyKind(month.StartOfMonth().LastFridayOfMonth().AddHours(18), DateTimeKind.Local)
        };
    }

    public static List<string> AssignPeopleToEvents(List<string> people, bool respectOrder, Random random)
    {
        if (respectOrder)
        {
            return people.ToList();
        }
        
        List<string> shuffled = people.ToList();
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        return shuffled;
    }

    public static void SetDefaultMeetupTime(MovieEvent movieEvent)
    {
        if (!movieEvent.MeetupTime.HasValue)
        {
            movieEvent.MeetupTime = DateTime.SpecifyKind(movieEvent.StartDate.StartOfMonth().LastFridayOfMonth().AddHours(18), DateTimeKind.Local);
        }
    }
}