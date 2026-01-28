using MovieReviewApp.Infrastructure.Repositories;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Service responsible for managing individual category votes.
/// Handles CRUD operations for member votes on award categories.
/// </summary>
public class CategoryVoteService(
    IRepository<CategoryVote> repository,
    ILogger<CategoryVoteService> logger)
    : BaseService<CategoryVote>(repository, logger)
{
    /// <summary>
    /// Gets all votes for a specific category voting event
    /// </summary>
    public async Task<List<CategoryVote>> GetVotesForEventAsync(Guid eventId)
    {
        List<CategoryVote> allVotes = await GetAllAsync();
        return allVotes
            .Where(v => v.CategoryVotingEventId == eventId)
            .ToList();
    }

    /// <summary>
    /// Gets a specific user's vote for an event
    /// </summary>
    public async Task<CategoryVote?> GetVoteByUserAsync(Guid eventId, string voterName)
    {
        List<CategoryVote> allVotes = await GetAllAsync();
        return allVotes
            .Where(v => v.CategoryVotingEventId == eventId &&
                       v.VoterName.Equals(voterName, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
    }

    /// <summary>
    /// Checks if a user has already voted for an event
    /// </summary>
    public async Task<bool> HasUserVotedAsync(Guid eventId, string voterName)
    {
        CategoryVote? vote = await GetVoteByUserAsync(eventId, voterName);
        return vote != null;
    }

    /// <summary>
    /// Casts a vote for a user (creates or updates their vote)
    /// </summary>
    public async Task<CategoryVote> CastVoteAsync(
        Guid eventId,
        string voterName,
        string voterIp,
        Dictionary<string, int> categoryRatings)
    {
        // Check if user already voted
        CategoryVote? existingVote = await GetVoteByUserAsync(eventId, voterName);

        if (existingVote != null)
        {
            // Update existing vote
            existingVote.CategoryRatings = categoryRatings;
            existingVote.VotedAt = DateTime.UtcNow;
            existingVote.VoterIp = voterIp;
            return await UpdateAsync(existingVote);
        }

        // Create new vote
        CategoryVote newVote = new CategoryVote
        {
            CategoryVotingEventId = eventId,
            VoterName = voterName,
            VoterIp = voterIp,
            CategoryRatings = categoryRatings,
            VotedAt = DateTime.UtcNow
        };

        return await CreateAsync(newVote);
    }

    /// <summary>
    /// Gets vote counts for all categories in an event
    /// </summary>
    public async Task<List<CategoryVoteResult>> GetVoteCountsAsync(Guid eventId)
    {
        List<CategoryVote> votes = await GetVotesForEventAsync(eventId);

        // Aggregate ratings into point totals and counts
        Dictionary<string, CategoryVoteResult> categoryTotals = new(StringComparer.OrdinalIgnoreCase);

        foreach (CategoryVote vote in votes)
        {
            foreach (KeyValuePair<string, int> rating in vote.CategoryRatings)
            {
                string category = rating.Key;
                int points = rating.Value;

                if (!categoryTotals.TryGetValue(category, out CategoryVoteResult? result))
                {
                    result = new CategoryVoteResult
                    {
                        CategoryName = category
                    };
                    categoryTotals[category] = result;
                }

                result.TotalPoints += points;
                if (points == 2)
                {
                    result.LoveCount++;
                }
                else if (points == 1)
                {
                    result.LikeCount++;
                }
                else if (points == 0)
                {
                    result.DontLikeCount++;
                }

                result.VoterRatings[vote.VoterName] = points;
            }
        }

        return categoryTotals.Values
            .OrderByDescending(r => r.TotalPoints)
            .ThenByDescending(r => r.LoveCount)
            .ThenBy(r => r.CategoryName)
            .ToList();
    }

    /// <summary>
    /// Gets the top N categories by vote count
    /// </summary>
    public async Task<List<string>> GetTopCategoriesAsync(Guid eventId, int count = 12)
    {
        List<CategoryVoteResult> results = await GetVoteCountsAsync(eventId);
        return results
            .Take(count)
            .Select(r => r.CategoryName)
            .ToList();
    }
}
