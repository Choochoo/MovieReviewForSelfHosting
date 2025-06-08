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
    public class AwardEventRepository : BaseService<AwardEvent>, IAwardEventService
    {
        public AwardEventRepository(
            IDatabaseService databaseService,
            ILogger<AwardEventRepository> logger) : base(databaseService, logger)
        {
        }

        public async Task<List<AwardEvent>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var events = await GetAllAsync();
                return events
                    .Where(e => e.StartDate >= startDate && e.EndDate <= endDate)
                    .OrderByDescending(e => e.StartDate)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get award events by date range");
                return new List<AwardEvent>();
            }
        }

        public async Task<AwardEvent?> GetCurrentEventAsync()
        {
            try
            {
                var events = await GetAllAsync();
                return events
                    .Where(e => e.StartDate <= DateTime.UtcNow && e.EndDate >= DateTime.UtcNow)
                    .OrderByDescending(e => e.StartDate)
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get current award event");
                return null;
            }
        }

        public async Task<List<AwardEvent>> GetUpcomingEventsAsync()
        {
            try
            {
                var events = await GetAllAsync();
                return events
                    .Where(e => e.StartDate > DateTime.UtcNow)
                    .OrderBy(e => e.StartDate)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get upcoming award events");
                return new List<AwardEvent>();
            }
        }

        public async Task<List<AwardEvent>> GetPastEventsAsync()
        {
            try
            {
                var events = await GetAllAsync();
                return events
                    .Where(e => e.EndDate < DateTime.UtcNow)
                    .OrderByDescending(e => e.EndDate)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get past award events");
                return new List<AwardEvent>();
            }
        }
    }
} 