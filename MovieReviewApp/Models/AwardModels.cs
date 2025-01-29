namespace MovieReviewApp.Models
{
    public class AwardQuestion
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Question { get; set; }
        public bool IsActive { get; set; } = true;
        public int MaxVotes { get; set; } = 3;
    }

    public class AwardEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsActive { get; set; } = true;
        public List<string> Questions { get; set; } = new();
        public List<Guid> EligibleMovieIds { get; set; } = new();
    }

    public class AwardVote
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string AwardEventId { get; set; }
        public string QuestionId { get; set; }
        public string MovieEventId { get; set; }
        public string VoterIp { get; set; }
        public DateTime VoteDate { get; set; }
        public int VoteOrder { get; set; }  // 1st=3pts, 2nd=2pts, 3rd=1pt

        public int GetPoints() => VoteOrder switch
        {
            1 => 3,
            2 => 2,
            3 => 1,
            _ => 0
        };
    }

    public class VoteResult
    {
        public string MovieEventId { get; set; }
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
    }
}