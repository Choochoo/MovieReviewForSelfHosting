using MovieReviewApp.Database;
using MovieReviewApp.Models;

namespace MovieReviewApp.Services
{
    public class SiteUpdateService
    {
        private readonly MongoDbService _mongoDbService;
        private readonly ILogger<SiteUpdateService> _logger;

        public SiteUpdateService(
            MongoDbService mongoDbService,
            ILogger<SiteUpdateService> logger)
        {
            _mongoDbService = mongoDbService;
            _logger = logger;
        }

        public async Task<List<SiteUpdate>> GetAllAsync()
        {
            try
            {
                var updates = await _mongoDbService.GetAllAsync<SiteUpdate>();
                return updates.OrderByDescending(u => u.Timestamp).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all site updates");
                return new List<SiteUpdate>();
            }
        }

        public async Task<SiteUpdate?> GetByIdAsync(string id)
        {
            try
            {
                return await _mongoDbService.GetByIdAsync<SiteUpdate>(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get site update by id {Id}", id);
                return null;
            }
        }

        public async Task<List<SiteUpdate>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var updates = await _mongoDbService.GetAllAsync<SiteUpdate>();
                return updates
                    .Where(u => u.Date >= startDate && u.Date <= endDate)
                    .OrderByDescending(u => u.Timestamp)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get site updates by date range");
                return new List<SiteUpdate>();
            }
        }

        public async Task<List<SiteUpdate>> GetByTypeAsync(string updateType)
        {
            try
            {
                var updates = await _mongoDbService.GetAllAsync<SiteUpdate>();
                return updates
                    .Where(u => u.UpdateType == updateType)
                    .OrderByDescending(u => u.Timestamp)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get site updates by type {UpdateType}", updateType);
                return new List<SiteUpdate>();
            }
        }

        public async Task<SiteUpdate> CreateAsync(SiteUpdate update)
        {
            try
            {
                update.Timestamp = DateTime.UtcNow;
                update.LastUpdateTime = DateTime.UtcNow;
                await _mongoDbService.InsertAsync(update);
                _logger.LogInformation("Created site update: {Description}", update.Description);
                return update;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create site update: {Description}", update.Description);
                throw;
            }
        }

        public async Task<SiteUpdate> UpdateAsync(SiteUpdate update)
        {
            try
            {
                update.LastUpdateTime = DateTime.UtcNow;
                await _mongoDbService.UpsertAsync(update);
                _logger.LogInformation("Updated site update: {Description}", update.Description);
                return update;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update site update: {Description}", update.Description);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string id)
        {
            try
            {
                var update = await GetByIdAsync(id);
                if (update == null)
                    return false;

                await _mongoDbService.DeleteAsync<SiteUpdate>(id);
                _logger.LogInformation("Deleted site update {Id}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete site update {Id}", id);
                return false;
            }
        }

        public async Task<DateTime> GetLastUpdateTimeAsync()
        {
            try
            {
                var updates = await _mongoDbService.GetAllAsync<SiteUpdate>();
                return updates
                    .OrderByDescending(u => u.LastUpdateTime)
                    .FirstOrDefault()?.LastUpdateTime ?? DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get last update time");
                return DateTime.UtcNow;
            }
        }
    }
} 