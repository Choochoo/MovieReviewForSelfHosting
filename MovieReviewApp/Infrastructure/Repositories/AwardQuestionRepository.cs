using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Models;

namespace MovieReviewApp.Infrastructure.Repositories
{
    public class AwardQuestionRepository
    {
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<AwardQuestionRepository> _logger;

        public AwardQuestionRepository(
            IDatabaseService databaseService,
            ILogger<AwardQuestionRepository> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        public async Task<List<AwardQuestion>> GetAllAsync()
        {
            try
            {
                return await _databaseService.GetAllAsync<AwardQuestion>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all award questions");
                return new List<AwardQuestion>();
            }
        }

        public async Task<AwardQuestion?> GetByIdAsync(string id)
        {
            try
            {
                return await _databaseService.GetByIdAsync<AwardQuestion>(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get award question by id {Id}", id);
                return null;
            }
        }

        public async Task<List<AwardQuestion>> GetActiveAsync()
        {
            try
            {
                var questions = await _databaseService.GetAllAsync<AwardQuestion>();
                return questions.Where(q => q.IsActive).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get active award questions");
                return new List<AwardQuestion>();
            }
        }

        public async Task<AwardQuestion> CreateAsync(AwardQuestion question)
        {
            try
            {
                await _databaseService.InsertAsync(question);
                _logger.LogInformation("Created award question: {Question}", question.Question);
                return question;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create award question: {Question}", question.Question);
                throw;
            }
        }

        public async Task<AwardQuestion> UpdateAsync(AwardQuestion question)
        {
            try
            {
                await _databaseService.UpsertAsync(question);
                _logger.LogInformation("Updated award question: {Question}", question.Question);
                return question;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update award question: {Question}", question.Question);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string id)
        {
            try
            {
                var question = await GetByIdAsync(id);
                if (question == null)
                    return false;

                await _databaseService.DeleteAsync<AwardQuestion>(id);
                _logger.LogInformation("Deleted award question {Id}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete award question {Id}", id);
                return false;
            }
        }

        public async Task<bool> ToggleActiveAsync(string id)
        {
            try
            {
                var question = await GetByIdAsync(id);
                if (question == null)
                    return false;

                question.IsActive = !question.IsActive;
                await _databaseService.UpsertAsync(question);
                _logger.LogInformation("Toggled active status for award question {Id} to {IsActive}", id, question.IsActive);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle active status for award question {Id}", id);
                return false;
            }
        }

        public async Task<List<AwardQuestion>> GetByMaxVotesAsync(int maxVotes)
        {
            try
            {
                var questions = await _databaseService.GetAllAsync<AwardQuestion>();
                return questions
                    .Where(q => q.MaxVotes == maxVotes)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get award questions by max votes {MaxVotes}", maxVotes);
                return new List<AwardQuestion>();
            }
        }
    }
} 