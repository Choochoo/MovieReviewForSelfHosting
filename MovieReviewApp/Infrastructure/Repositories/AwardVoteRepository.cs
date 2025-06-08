using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Core.Services;
using MovieReviewApp.Models;

namespace MovieReviewApp.Infrastructure.Repositories
{
    public class AwardVoteRepository : BaseService<AwardVote>, IAwardVoteService
    {
        public AwardVoteRepository(
            IDatabaseService databaseService,
            ILogger<AwardVoteRepository> logger) : base(databaseService, logger)
        {
        }

        public async Task<List<AwardVote>> GetByEventIdAsync(string eventId)
        {
            try
            {
                var votes = await GetAllAsync();
                return votes
                    .Where(v => v.AwardEventId.ToString() == eventId)
                    .OrderByDescending(v => v.CreatedAt)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get votes for event {EventId}", eventId);
                return new List<AwardVote>();
            }
        }

        public async Task<List<AwardVote>> GetByUserIdAsync(string userId)
        {
            try
            {
                var votes = await GetAllAsync();
                return votes
                    .Where(v => v.VoterName == userId)
                    .OrderByDescending(v => v.CreatedAt)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get votes for user {UserId}", userId);
                return new List<AwardVote>();
            }
        }

        public async Task<List<AwardVote>> GetByCategoryIdAsync(string categoryId)
        {
            try
            {
                var votes = await GetAllAsync();
                return votes
                    .Where(v => v.QuestionId.ToString() == categoryId)
                    .OrderByDescending(v => v.CreatedAt)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get votes for category {CategoryId}", categoryId);
                return new List<AwardVote>();
            }
        }

        public async Task<AwardVote?> GetByUserAndCategoryAsync(string userId, string categoryId)
        {
            try
            {
                var votes = await GetAllAsync();
                return votes
                    .FirstOrDefault(v => v.VoterName == userId && v.QuestionId.ToString() == categoryId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get vote for user {UserId} and category {CategoryId}", userId, categoryId);
                return null;
            }
        }

        public async Task<List<QuestionResult>> GetResultsByEventIdAsync(Guid eventId)
        {
            try
            {
                var votes = await GetByEventIdAsync(eventId.ToString());
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