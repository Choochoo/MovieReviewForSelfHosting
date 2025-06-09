using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Models;

namespace MovieReviewApp.Infrastructure.Repositories
{
    public class DiscussionQuestionRepository
    {
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<DiscussionQuestionRepository> _logger;

        public DiscussionQuestionRepository(
            IDatabaseService databaseService,
            ILogger<DiscussionQuestionRepository> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        public async Task<List<DiscussionQuestion>> GetAllAsync()
        {
            try
            {
                IEnumerable<DiscussionQuestion> questions = await _databaseService.GetAllAsync<DiscussionQuestion>();
                return questions.OrderBy(q => q.Order).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all discussion questions");
                return new List<DiscussionQuestion>();
            }
        }

        public async Task<List<DiscussionQuestion>> GetActiveAsync()
        {
            try
            {
                IEnumerable<DiscussionQuestion> questions = await _databaseService.GetAllAsync<DiscussionQuestion>();
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

        public async Task<DiscussionQuestion?> GetByIdAsync(string id)
        {
            try
            {
                return await _databaseService.GetByIdAsync<DiscussionQuestion>(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get discussion question by id {Id}", id);
                return null;
            }
        }

        public async Task<DiscussionQuestion> CreateAsync(DiscussionQuestion question)
        {
            try
            {
                await _databaseService.InsertAsync(question);
                _logger.LogInformation("Created discussion question: {Question}", question.Question);
                return question;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create discussion question: {Question}", question.Question);
                throw;
            }
        }

        public async Task<DiscussionQuestion> UpdateAsync(DiscussionQuestion question)
        {
            try
            {
                question.UpdatedAt = DateTime.UtcNow;
                await _databaseService.UpsertAsync(question);
                _logger.LogInformation("Updated discussion question: {Question}", question.Question);
                return question;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update discussion question: {Question}", question.Question);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string id)
        {
            try
            {
                DiscussionQuestion? question = await GetByIdAsync(id);
                if (question == null)
                    return false;

                await _databaseService.DeleteAsync<DiscussionQuestion>(id);
                _logger.LogInformation("Deleted discussion question {Id}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete discussion question {Id}", id);
                return false;
            }
        }

        public async Task<bool> ToggleActiveAsync(string id)
        {
            try
            {
                DiscussionQuestion? question = await GetByIdAsync(id);
                if (question == null)
                    return false;

                question.IsActive = !question.IsActive;
                question.UpdatedAt = DateTime.UtcNow;
                await _databaseService.UpsertAsync(question);
                _logger.LogInformation("Toggled active status for discussion question {Id} to {IsActive}", id, question.IsActive);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle active status for discussion question {Id}", id);
                return false;
            }
        }

        public async Task<bool> UpdateOrderAsync(string id, int newOrder)
        {
            try
            {
                DiscussionQuestion? question = await GetByIdAsync(id);
                if (question == null)
                    return false;

                question.Order = newOrder;
                question.UpdatedAt = DateTime.UtcNow;
                await _databaseService.UpsertAsync(question);
                _logger.LogInformation("Updated order for discussion question {Id} to {Order}", id, newOrder);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update order for discussion question {Id}", id);
                return false;
            }
        }
    }
} 