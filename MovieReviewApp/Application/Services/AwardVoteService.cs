using MovieReviewApp.Models;
using Microsoft.Extensions.Logging;
using MovieReviewApp.Infrastructure.Database;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MovieReviewApp.Application.Services;

public class AwardVoteService : BaseService<AwardVote>
{
    private readonly AwardQuestionService _awardQuestionService;

    public AwardVoteService(
        MongoDbService databaseService,
        ILogger<AwardVoteService> logger,
        AwardQuestionService awardQuestionService)
        : base(databaseService, logger)
    {
        _awardQuestionService = awardQuestionService;
    }

    public async Task<List<AwardVote>> GetVotesAsync(Guid awardEventId, Guid? questionId = null)
    {
        List<AwardVote> votes = await GetAllAsync();
        return votes.Where(v => v.AwardEventId == awardEventId && (!questionId.HasValue || v.QuestionId == questionId.Value)).ToList();
    }

    public async Task<bool> DeleteVoteAsync(Guid awardEventId, Guid questionId, string voterName, Guid movieEventId)
    {
        List<AwardVote> votes = await GetAllAsync();
        AwardVote? vote = votes.FirstOrDefault(v =>
            v.AwardEventId == awardEventId &&
            v.QuestionId == questionId &&
            v.VoterName == voterName &&
            v.MovieEventId == movieEventId);

        if (vote != null)
        {
            return await DeleteAsync(vote.Id);
        }

        return false;
    }

    public async Task<long> DeleteAllVotesForEventAsync(Guid awardEventId)
    {
        List<AwardVote> votes = await GetAllAsync();
        long deletedCount = 0;

        foreach (var vote in votes.Where(v => v.AwardEventId == awardEventId))
        {
            if (await DeleteAsync(vote.Id))
            {
                deletedCount++;
            }
        }

        return deletedCount;
    }

    public async Task<Dictionary<Guid, int>> GetVoteCountsAsync(Guid awardEventId, Guid questionId)
    {
        List<AwardVote> votes = await GetAllAsync();
        return votes.Where(v => v.AwardEventId == awardEventId && v.QuestionId == questionId)
                   .GroupBy(v => v.MovieEventId)
                   .ToDictionary(g => g.Key, g => g.Count());
    }

    public async Task<List<string>> GetAvailableVotersAsync(Guid awardEventId)
    {
        List<AwardVote> votes = await GetAllAsync();
        return votes.Where(v => v.AwardEventId == awardEventId)
                   .Select(v => v.VoterName)
                   .Distinct()
                   .ToList();
    }

    public async Task<Dictionary<Guid, int>> GetRemainingVotesForUserAsync(string userName, Guid awardEventId, List<AwardQuestion> questions)
    {
        List<AwardVote> votes = await GetAllAsync();
        IEnumerable<AwardVote> userVotes = votes.Where(v => v.VoterName == userName && v.AwardEventId == awardEventId);

        Dictionary<Guid, int> result = new Dictionary<Guid, int>();
        foreach (var question in questions)
        {
            int voteCount = userVotes.Count(v => v.QuestionId == question.Id);
            result[question.Id] = Math.Max(0, question.MaxVotes - voteCount);
        }
        return result;
    }

    public async Task<List<(AwardQuestion Question, int RemainingVotes)>> GetAvailableQuestionsForUserAsync(string userName, Guid awardEventId)
    {
        List<AwardQuestion> questions = await _awardQuestionService.GetActiveAwardQuestionsAsync();
        Dictionary<Guid, int> remainingVotes = await GetRemainingVotesForUserAsync(userName, awardEventId, questions);

        return questions
            .Where(q => remainingVotes.GetValueOrDefault(q.Id, 0) > 0)
            .Select(q => (q, remainingVotes.GetValueOrDefault(q.Id, 0)))
            .ToList();
    }
} 