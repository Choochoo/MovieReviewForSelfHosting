using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MovieReviewApp.Models;

namespace MovieReviewApp.Core.Interfaces
{
    public interface IAwardVoteService : IBaseService<AwardVote>
    {
        Task<List<AwardVote>> GetByEventIdAsync(string eventId);
        Task<List<AwardVote>> GetByUserIdAsync(string userId);
        Task<List<AwardVote>> GetByCategoryIdAsync(string categoryId);
        Task<AwardVote?> GetByUserAndCategoryAsync(string userId, string categoryId);
    }
} 