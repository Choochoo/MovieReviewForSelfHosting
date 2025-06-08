using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MovieReviewApp.Models;

namespace MovieReviewApp.Core.Interfaces
{
    public interface IAwardEventService : IBaseService<AwardEvent>
    {
        Task<List<AwardEvent>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
        Task<AwardEvent?> GetCurrentEventAsync();
        Task<List<AwardEvent>> GetUpcomingEventsAsync();
        Task<List<AwardEvent>> GetPastEventsAsync();
    }
} 