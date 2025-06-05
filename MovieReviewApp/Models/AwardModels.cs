using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MovieReviewApp.Attributes;

namespace MovieReviewApp.Models
{
    [MongoCollection("AwardEvents")]
    public class AwardEvent : BaseModel
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        [BsonGuidRepresentation(GuidRepresentation.CSharpLegacy)]
        public List<Guid> Questions { get; set; } = new();
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime VotingStartDate { get; set; }
        public DateTime VotingEndDate { get; set; }
        public int PhaseNumber { get; set; }
    }

    [MongoCollection("AwardQuestions")]
    public class AwardQuestion : BaseModel
    {
        public string Question { get; set; } = string.Empty;
        public int MaxVotes { get; set; }
        public bool IsActive { get; set; }
    }

    [MongoCollection("AwardVotes")]
    public class AwardVote : BaseModel
    {
        [BsonGuidRepresentation(GuidRepresentation.CSharpLegacy)]
        public Guid AwardEventId { get; set; }
        [BsonGuidRepresentation(GuidRepresentation.CSharpLegacy)]
        public Guid QuestionId { get; set; }
        [BsonGuidRepresentation(GuidRepresentation.CSharpLegacy)]
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