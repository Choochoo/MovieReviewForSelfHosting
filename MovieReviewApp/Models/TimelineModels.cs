using MovieReviewApp.Models;

namespace MovieReviewApp.Models
{
    public interface ITimelineItem
    {
        DateTime Date { get; }
    }

    public class PhaseTimelineItem : ITimelineItem
    {
        public Phase Phase { get; }
        public DateTime Date => Phase.StartDate;

        public PhaseTimelineItem(Phase phase)
        {
            Phase = phase;
        }
    }

    public class AwardTimelineItem : ITimelineItem
    {
        public AwardEvent AwardEvent { get; }
        public DateTime Date => AwardEvent.StartDate;

        public AwardTimelineItem(AwardEvent awardEvent)
        {
            AwardEvent = awardEvent;
        }
    }

    public class FutureAwardTimelineItem : ITimelineItem
    {
        public FutureAwardItem FutureAward { get; }
        public DateTime Date => FutureAward.AwardDate;

        public FutureAwardTimelineItem(FutureAwardItem futureAward)
        {
            FutureAward = futureAward;
        }
    }

    public class FutureAwardItem
    {
        public int PhaseNumber { get; set; }
        public DateTime AwardDate { get; set; }
    }
}