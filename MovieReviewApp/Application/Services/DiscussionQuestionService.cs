using MovieReviewApp.Infrastructure.Repositories;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

public class DiscussionQuestionService(IRepository<DiscussionQuestion> repository, ILogger<DiscussionQuestionService> logger)
    : BaseService<DiscussionQuestion>(repository, logger)
{
    public async Task<List<DiscussionQuestion>> GetActiveQuestionsAsync()
    {
        List<DiscussionQuestion> questions = await GetAllAsync();
        return questions
            .Where(q => q.IsActive)
            .OrderBy(q => q.Order)
            .ToList();
    }

    public async Task<List<DiscussionQuestion>> GetAllQuestionsAsync()
    {
        try
        {
            List<DiscussionQuestion> questions = await GetAllAsync();

            // If no questions exist, create defaults
            if (!questions.Any())
            {
                await CreateDefaultQuestionsAsync();
                questions = await GetAllAsync();
            }

            return questions.OrderBy(q => q.Order).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get all discussion questions");
            return new List<DiscussionQuestion>();
        }
    }

    private async Task CreateDefaultQuestionsAsync()
    {
        try
        {
            string[] defaultQuestions = new[]
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
                DiscussionQuestion question = new DiscussionQuestion
                {
                    Question = defaultQuestions[i],
                    Order = i + 1,
                    IsActive = true,
                };

                _ = await CreateAsync(question);
            }

            logger.LogInformation("Created {Count} default discussion questions", defaultQuestions.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create default discussion questions");
            throw;
        }
    }

    public async Task<List<string>> GetQuestionTextsForPromptAsync()
    {
        List<DiscussionQuestion> questions = await GetActiveQuestionsAsync();
        return questions.Select(q => q.Question).ToList();
    }
}
