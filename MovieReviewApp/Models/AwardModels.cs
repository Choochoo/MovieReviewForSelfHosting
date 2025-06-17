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
        public List<Guid> Questions { get; set; } = [];
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
        public string VoterIp { get; set; } = string.Empty;
        public string VoterName { get; set; }
        public int Points { get; set; }
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
        public int PhasesBeforeAward { get; set; }
        public bool AwardsEnabled { get; set; }
        public bool ShowResultsDuringVoting { get; set; }
        public bool AllowVoteChanges { get; set; }
        public int VoteChangeTimeLimit { get; set; }

        public AwardSetting()
        {
            // Default values are now managed through ApplicationSettings
            // These will be set by the service layer
        }

        public static AwardSetting CreateFromApplicationSettings(ApplicationSettings appSettings)
        {
            return new AwardSetting
            {
                PhasesBeforeAward = appSettings.DefaultPhasesBeforeAward,
                AwardsEnabled = appSettings.DefaultAwardsEnabled,
                ShowResultsDuringVoting = appSettings.DefaultShowResultsDuringVoting,
                AllowVoteChanges = appSettings.DefaultAllowVoteChanges,
                VoteChangeTimeLimit = appSettings.DefaultVoteChangeTimeLimit
            };
        }
    }
}
