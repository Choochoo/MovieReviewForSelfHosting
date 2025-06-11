using MovieReviewApp.Models;
using Microsoft.Extensions.Logging;
using MovieReviewApp.Infrastructure.Database;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MovieReviewApp.Application.Services;

public class AwardQuestionService(MongoDbService databaseService, ILogger<AwardQuestionService> logger)
    : BaseService<AwardQuestion>(databaseService, logger)
{
    public async Task<List<AwardQuestion>> GetActiveAwardQuestionsAsync()
    {
        List<AwardQuestion> questions = await GetAllAsync();
        return questions.Where(q => q.IsActive).ToList();
    }

    public async Task DeleteDefaultQuestionsAsync()
    {
        List<AwardQuestion> defaultQuestions = await GetAllAsync();
        defaultQuestions = defaultQuestions.Where(q => q.Question.Contains("Default")).ToList();
        foreach (AwardQuestion question in defaultQuestions)
        {
            await DeleteAsync(question.Id);
        }
    }

    public async Task<List<QuestionResult>> GetQuestionResultsAsync(Guid awardEventId, Guid questionId)
    {
        List<AwardVote> votes = await _db.GetAllAsync<AwardVote>();
        List<MovieEvent> events = await _db.GetAllAsync<MovieEvent>();

        List<QuestionResult> results = votes
            .Where(v => v.AwardEventId == awardEventId && v.QuestionId == questionId)
            .GroupBy(v => v.MovieEventId)
            .Select(g =>
            {
                MovieEvent? movieEvent = events.FirstOrDefault(e => e.Id == g.Key);
                string movieTitle = movieEvent?.Movie ?? "Unknown Movie";
                return new QuestionResult
                {
                    MovieTitle = movieTitle,
                    TotalPoints = g.Sum(v => v.Points),
                    FirstPlaceVotes = g.Count(v => v.Points == 3),
                    SecondPlaceVotes = g.Count(v => v.Points == 2),
                    ThirdPlaceVotes = g.Count(v => v.Points == 1)
                };
            })
            .OrderByDescending(r => r.TotalPoints)
            .ThenByDescending(r => r.FirstPlaceVotes)
            .ThenByDescending(r => r.SecondPlaceVotes)
            .ToList();

        return results;
    }
}