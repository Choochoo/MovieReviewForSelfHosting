using MovieReviewApp.Database;
using MovieReviewApp.Models;

namespace MovieReviewApp.Services
{
    public class AwardEventService
    {
        private readonly MongoDbService _mongoDbService;
        private readonly ILogger<AwardEventService> _logger;

        public AwardEventService(
            MongoDbService mongoDbService,
            ILogger<AwardEventService> logger)
        {
            _mongoDbService = mongoDbService;
            _logger = logger;
        }

        public async Task<List<AwardEvent>> GetAllAsync()
        {
            try
            {
                return await _mongoDbService.GetAllAsync<AwardEvent>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all award events");
                return new List<AwardEvent>();
            }
        }

        public async Task<AwardEvent?> GetByIdAsync(string id)
        {
            try
            {
                return await _mongoDbService.GetByIdAsync<AwardEvent>(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get award event by id {Id}", id);
                return null;
            }
        }

        public async Task<List<AwardEvent>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var events = await _mongoDbService.GetAllAsync<AwardEvent>();
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

        public async Task<AwardEvent> CreateAsync(AwardEvent awardEvent)
        {
            try
            {
                await _mongoDbService.InsertAsync(awardEvent);
                _logger.LogInformation("Created award event for phase {PhaseNumber}", awardEvent.PhaseNumber);
                return awardEvent;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create award event for phase {PhaseNumber}", awardEvent.PhaseNumber);
                throw;
            }
        }

        public async Task<AwardEvent> UpdateAsync(AwardEvent awardEvent)
        {
            try
            {
                await _mongoDbService.UpsertAsync(awardEvent);
                _logger.LogInformation("Updated award event for phase {PhaseNumber}", awardEvent.PhaseNumber);
                return awardEvent;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update award event for phase {PhaseNumber}", awardEvent.PhaseNumber);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string id)
        {
            try
            {
                var awardEvent = await GetByIdAsync(id);
                if (awardEvent == null)
                    return false;

                await _mongoDbService.DeleteAsync<AwardEvent>(id);
                _logger.LogInformation("Deleted award event {Id}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete award event {Id}", id);
                return false;
            }
        }

        public async Task<AwardEvent?> GetCurrentEventAsync()
        {
            try
            {
                var events = await _mongoDbService.GetAllAsync<AwardEvent>();
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
                var events = await _mongoDbService.GetAllAsync<AwardEvent>();
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
                var events = await _mongoDbService.GetAllAsync<AwardEvent>();
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