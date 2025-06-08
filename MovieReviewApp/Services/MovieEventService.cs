using MovieReviewApp.Database;
using MovieReviewApp.Models;

namespace MovieReviewApp.Services
{
    public class MovieEventService
    {
        private readonly MongoDbService _mongoDbService;
        private readonly ILogger<MovieEventService> _logger;

        public MovieEventService(
            MongoDbService mongoDbService,
            ILogger<MovieEventService> logger)
        {
            _mongoDbService = mongoDbService;
            _logger = logger;
        }

        public async Task<List<MovieEvent>> GetAllAsync()
        {
            try
            {
                return await _mongoDbService.GetAllAsync<MovieEvent>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all movie events");
                return new List<MovieEvent>();
            }
        }

        public async Task<MovieEvent?> GetByIdAsync(string id)
        {
            try
            {
                return await _mongoDbService.GetByIdAsync<MovieEvent>(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get movie event by id {Id}", id);
                return null;
            }
        }

        public async Task<List<MovieEvent>> GetByPhaseAsync(int phaseNumber)
        {
            try
            {
                var events = await _mongoDbService.GetAllAsync<MovieEvent>();
                return events.Where(e => e.PhaseNumber == phaseNumber).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get movie events for phase {PhaseNumber}", phaseNumber);
                return new List<MovieEvent>();
            }
        }

        public async Task<MovieEvent> CreateAsync(MovieEvent movieEvent)
        {
            try
            {
                await _mongoDbService.InsertAsync(movieEvent);
                _logger.LogInformation("Created movie event for {Movie}", movieEvent.Movie);
                return movieEvent;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create movie event for {Movie}", movieEvent.Movie);
                throw;
            }
        }

        public async Task<MovieEvent> UpdateAsync(MovieEvent movieEvent)
        {
            try
            {
                await _mongoDbService.UpsertAsync(movieEvent);
                _logger.LogInformation("Updated movie event for {Movie}", movieEvent.Movie);
                return movieEvent;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update movie event for {Movie}", movieEvent.Movie);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string id)
        {
            try
            {
                var movieEvent = await GetByIdAsync(id);
                if (movieEvent == null)
                    return false;

                await _mongoDbService.DeleteAsync<MovieEvent>(id);
                _logger.LogInformation("Deleted movie event {Id}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete movie event {Id}", id);
                return false;
            }
        }

        public async Task<List<MovieEvent>> GetUpcomingEventsAsync()
        {
            try
            {
                var events = await _mongoDbService.GetAllAsync<MovieEvent>();
                return events
                    .Where(e => e.StartDate > DateTime.UtcNow)
                    .OrderBy(e => e.StartDate)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get upcoming movie events");
                return new List<MovieEvent>();
            }
        }

        public async Task<List<MovieEvent>> GetPastEventsAsync()
        {
            try
            {
                var events = await _mongoDbService.GetAllAsync<MovieEvent>();
                return events
                    .Where(e => e.EndDate < DateTime.UtcNow)
                    .OrderByDescending(e => e.EndDate)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get past movie events");
                return new List<MovieEvent>();
            }
        }
    }
} 