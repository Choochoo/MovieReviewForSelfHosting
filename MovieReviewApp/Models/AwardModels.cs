namespace MovieReviewApp.Models
{

    public class AwardEvent : BaseModel
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<Guid> Questions { get; set; } = new();
    }

    public class AwardQuestion : BaseModel
    {
        public string Question { get; set; }
        public int MaxVotes { get; set; }
        public bool IsActive { get; set; }
    }

    public class AwardVote : BaseModel
    {
        public Guid AwardEventId { get; set; }
        public Guid QuestionId { get; set; }
        public Guid MovieEventId { get; set; }
        public string VoterIp { get; set; }
        public string VoterName { get; set; }
        public int Points { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class QuestionResult
    {
        public string MovieTitle { get; set; }
        public int TotalPoints { get; set; }
        public int FirstPlaceVotes { get; set; }
        public int SecondPlaceVotes { get; set; }
        public int ThirdPlaceVotes { get; set; }
    }

    public class AwardSetting
    {
        public int PhasesBeforeAward { get; set; } = 2;
        public bool AwardsEnabled { get; set; } = false;
        public bool ShowResultsDuringVoting { get; set; } = false;
        public bool AllowVoteChanges { get; set; } = true;
        public int VoteChangeTimeLimit { get; set; } = 24; // hours
    }
}