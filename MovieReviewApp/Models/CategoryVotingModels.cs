using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MovieReviewApp.Attributes;

namespace MovieReviewApp.Models;

/// <summary>
/// Represents a category voting event that occurs in the month before an awards month.
/// Members vote on AI-generated categories to determine the final 12 award categories.
/// </summary>
[MongoCollection("CategoryVotingEvents")]
public class CategoryVotingEvent : BaseModel
{
    /// <summary>
    /// The month this voting event is for (pre-awards month)
    /// </summary>
    public DateTime Month { get; set; }

    /// <summary>
    /// When the AI generated the categories
    /// </summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>
    /// The 40 AI-generated categories to vote on
    /// </summary>
    public List<string> GeneratedCategories { get; set; } = new();

    /// <summary>
    /// When voting opens
    /// </summary>
    public DateTime VotingStartDate { get; set; }

    /// <summary>
    /// When voting closes
    /// </summary>
    public DateTime VotingEndDate { get; set; }

    /// <summary>
    /// The phase number of the award event this feeds into
    /// </summary>
    public int TargetAwardEventPhaseNumber { get; set; }

    /// <summary>
    /// True when voting is complete and final categories have been selected
    /// </summary>
    public bool IsFinalized { get; set; }

    /// <summary>
    /// The top 12 categories after voting is finalized
    /// </summary>
    public List<string> FinalCategories { get; set; } = new();
}

/// <summary>
/// Represents a member's vote on award categories.
/// Each member rates every category with a point value.
/// </summary>
[MongoCollection("CategoryVotes")]
public class CategoryVote : BaseModel
{
    /// <summary>
    /// The category voting event this vote belongs to
    /// </summary>
    [BsonGuidRepresentation(GuidRepresentation.CSharpLegacy)]
    public Guid CategoryVotingEventId { get; set; }

    /// <summary>
    /// The name of the person voting
    /// </summary>
    public string VoterName { get; set; } = string.Empty;

    /// <summary>
    /// The IP address of the voter (for audit trail)
    /// </summary>
    public string VoterIp { get; set; } = string.Empty;

    /// <summary>
    /// The rating per category (0 = Don't Like, 1 = Like, 2 = Love)
    /// </summary>
    public Dictionary<string, int> CategoryRatings { get; set; } = new();

    /// <summary>
    /// When the vote was cast
    /// </summary>
    public DateTime VotedAt { get; set; }
}

/// <summary>
/// Maps an IP address to a person's identity for category voting.
/// Once locked, prevents users from switching identities.
/// </summary>
[MongoCollection("VoterIdentities")]
public class VoterIdentity : BaseModel
{
    /// <summary>
    /// The IP address that is locked to this identity
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// The person's name this IP is locked to
    /// </summary>
    public string PersonName { get; set; } = string.Empty;

    /// <summary>
    /// When this identity was locked
    /// </summary>
    public DateTime LockedAt { get; set; }
}

/// <summary>
/// Represents the vote count for a single category
/// </summary>
public class CategoryVoteResult
{
    /// <summary>
    /// The category name
    /// </summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>
    /// Total points for this category
    /// </summary>
    public int TotalPoints { get; set; }

    /// <summary>
    /// Number of "Love" ratings (2 points)
    /// </summary>
    public int LoveCount { get; set; }

    /// <summary>
    /// Number of "Like" ratings (1 point)
    /// </summary>
    public int LikeCount { get; set; }

    /// <summary>
    /// Number of "Don't Like" ratings (0 points)
    /// </summary>
    public int DontLikeCount { get; set; }

    /// <summary>
    /// Map of voter name to rating
    /// </summary>
    public Dictionary<string, int> VoterRatings { get; set; } = new();
}
