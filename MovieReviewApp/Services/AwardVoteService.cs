using MovieReviewApp.Database;
using MovieReviewApp.Models;

namespace MovieReviewApp.Services
{
    public class AwardVoteService
    {
        private readonly MongoDbService _mongoDbService;
        private readonly ILogger<AwardVoteService> _logger;

        public AwardVoteService(
            MongoDbService mongoDbService,
            ILogger<AwardVoteService> logger)
        {
            _mongoDbService = mongoDbService;
            _logger = logger;
        }

        public async Task<List<AwardVote>> GetAllAsync()
        {
            try
            {
                return await _mongoDbService.GetAllAsync<AwardVote>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all award votes");
                return new List<AwardVote>();
            }
        }

        public async Task<AwardVote?> GetByIdAsync(string id)
        {
            try
            {
                return await _mongoDbService.GetByIdAsync<AwardVote>(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get award vote by id {Id}", id);
                return null;
            }
        }

        public async Task<List<AwardVote>> GetByEventIdAsync(Guid eventId)
        {
            try
            {
                var votes = await _mongoDbService.GetAllAsync<AwardVote>();
                return votes
                    .Where(v => v.AwardEventId == eventId)
                    .OrderByDescending(v => v.CreatedAt)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get award votes for event {EventId}", eventId);
                return new List<AwardVote>();
            }
        }

        public async Task<List<AwardVote>> GetByQuestionIdAsync(Guid questionId)
        {
            try
            {
                var votes = await _mongoDbService.GetAllAsync<AwardVote>();
                return votes
                    .Where(v => v.QuestionId == questionId)
                    .OrderByDescending(v => v.CreatedAt)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get award votes for question {QuestionId}", questionId);
                return new List<AwardVote>();
            }
        }

        public async Task<List<AwardVote>> GetByVoterIpAsync(string voterIp)
        {
            try
            {
                var votes = await _mongoDbService.GetAllAsync<AwardVote>();
                return votes
                    .Where(v => v.VoterIp == voterIp)
                    .OrderByDescending(v => v.CreatedAt)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get award votes for voter IP {VoterIp}", voterIp);
                return new List<AwardVote>();
            }
        }

        public async Task<AwardVote> CreateAsync(AwardVote vote)
        {
            try
            {
                await _mongoDbService.InsertAsync(vote);
                _logger.LogInformation("Created award vote for question {QuestionId} by {VoterName}", vote.QuestionId, vote.VoterName);
                return vote;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create award vote for question {QuestionId}", vote.QuestionId);
                throw;
            }
        }

        public async Task<AwardVote> UpdateAsync(AwardVote vote)
        {
            try
            {
                await _mongoDbService.UpsertAsync(vote);
                _logger.LogInformation("Updated award vote for question {QuestionId} by {VoterName}", vote.QuestionId, vote.VoterName);
                return vote;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update award vote for question {QuestionId}", vote.QuestionId);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string id)
        {
            try
            {
                var vote = await GetByIdAsync(id);
                if (vote == null)
                    return false;

                await _mongoDbService.DeleteAsync<AwardVote>(id);
                _logger.LogInformation("Deleted award vote {Id}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete award vote {Id}", id);
                return false;
            }
        }

        public async Task<List<QuestionResult>> GetResultsByEventIdAsync(Guid eventId)
        {
            try
            {
                var votes = await GetByEventIdAsync(eventId);
                var results = votes
                    .GroupBy(v => v.MovieEventId)
                    .Select(g => new QuestionResult
                    {
                        MovieTitle = g.First().MovieEventId.ToString(), // You might want to get the actual movie title from MovieEvent
                        TotalPoints = g.Sum(v => v.Points),
                        FirstPlaceVotes = g.Count(v => v.Points == 3),
                        SecondPlaceVotes = g.Count(v => v.Points == 2),
                        ThirdPlaceVotes = g.Count(v => v.Points == 1)
                    })
                    .OrderByDescending(r => r.TotalPoints)
                    .ToList();

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get results for event {EventId}", eventId);
                return new List<QuestionResult>();
            }
        }
    }
} 