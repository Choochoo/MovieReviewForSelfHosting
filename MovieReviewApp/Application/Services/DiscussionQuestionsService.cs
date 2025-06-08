using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Database;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

public class DiscussionQuestionsService
{
    private readonly IDatabaseService _mongoDbService;
    private readonly ILogger<DiscussionQuestionsService> _logger;

    public DiscussionQuestionsService(IDatabaseService mongoDbService, ILogger<DiscussionQuestionsService> logger)
    {
        _mongoDbService = mongoDbService;
        _logger = logger;
    }

    public async Task<List<DiscussionQuestion>> GetActiveQuestionsAsync()
    {
        try
        {
            var questions = await _mongoDbService.GetAllAsync<DiscussionQuestion>();

            // If no questions exist, create defaults
            if (!questions.Any())
            {
                await CreateDefaultQuestionsAsync();
                questions = await _mongoDbService.GetAllAsync<DiscussionQuestion>();
            }

            return questions
                .Where(q => q.IsActive)
                .OrderBy(q => q.Order)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active discussion questions");
            return new List<DiscussionQuestion>();
        }
    }

    public async Task<List<DiscussionQuestion>> GetAllQuestionsAsync()
    {
        try
        {
            var questions = await _mongoDbService.GetAllAsync<DiscussionQuestion>();

            // If no questions exist, create defaults
            if (!questions.Any())
            {
                await CreateDefaultQuestionsAsync();
                questions = await _mongoDbService.GetAllAsync<DiscussionQuestion>();
            }

            return questions.OrderBy(q => q.Order).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all discussion questions");
            return new List<DiscussionQuestion>();
        }
    }

    public async Task<DiscussionQuestion?> GetQuestionByIdAsync(string id)
    {
        try
        {
            return await _mongoDbService.GetByIdAsync<DiscussionQuestion>(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get discussion question by id {Id}", id);
            return null;
        }
    }

    public async Task<DiscussionQuestion> CreateQuestionAsync(string questionText, int order, bool isActive = true)
    {
        try
        {
            var question = new DiscussionQuestion
            {
                Question = questionText,
                Order = order,
                IsActive = isActive,
                CreatedAt = DateTime.UtcNow
            };

            await _mongoDbService.InsertAsync(question);
            _logger.LogInformation("Created discussion question: {Question}", questionText);
            return question;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create discussion question: {Question}", questionText);
            throw;
        }
    }

    public async Task<bool> UpdateQuestionAsync(DiscussionQuestion question)
    {
        try
        {
            question.UpdatedAt = DateTime.UtcNow;
            await _mongoDbService.UpsertAsync(question);
            _logger.LogInformation("Updated discussion question: {Id}", question.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update discussion question: {Id}", question.Id);
            return false;
        }
    }

    public async Task<bool> DeleteQuestionAsync(Guid id)
    {
        try
        {
            await _mongoDbService.DeleteByIdAsync<DiscussionQuestion>(id);
            _logger.LogInformation("Deleted discussion question: {Id}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete discussion question: {Id}", id);
            return false;
        }
    }

    public async Task<bool> ReorderQuestionsAsync(List<string> questionIds)
    {
        try
        {
            for (int i = 0; i < questionIds.Count; i++)
            {
                var question = await GetQuestionByIdAsync(questionIds[i]);
                if (question != null)
                {
                    question.Order = i + 1;
                    await UpdateQuestionAsync(question);
                }
            }

            _logger.LogInformation("Reordered {Count} discussion questions", questionIds.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reorder discussion questions");
            return false;
        }
    }

    private async Task CreateDefaultQuestionsAsync()
    {
        try
        {
            var defaultQuestions = new[]
            {
                "Did I like the movie?",
                "Am I glad I watched the movie?",
                "Do I think I'd ever watch it again?",
                "Would you ever recommend this movie?",
                "What was my favorite part of the movie?",
                "What was my least favorite part of the movie?",
                "What was my favorite line of the movie?"
            };

            for (int i = 0; i < defaultQuestions.Length; i++)
            {
                var question = new DiscussionQuestion
                {
                    Question = defaultQuestions[i],
                    Order = i + 1,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                await _mongoDbService.InsertAsync(question);
            }

            _logger.LogInformation("Created {Count} default discussion questions", defaultQuestions.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create default discussion questions");
            throw;
        }
    }

    public async Task<List<string>> GetQuestionTextsForPromptAsync()
    {
        var questions = await GetActiveQuestionsAsync();
        return questions.Select(q => q.Question).ToList();
    }
}