using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Models;

namespace MovieReviewApp.Infrastructure.Repositories
{
    public class MovieEventRepository
    {
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<MovieEventRepository> _logger;

        public MovieEventRepository(
            IDatabaseService databaseService,
            ILogger<MovieEventRepository> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        public async Task<List<MovieEvent>> GetAllAsync()
        {
            try
            {
                return await _databaseService.GetAllAsync<MovieEvent>();
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
                return await _databaseService.GetByIdAsync<MovieEvent>(id);
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
                IEnumerable<MovieEvent> events = await _databaseService.GetAllAsync<MovieEvent>();
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
                await _databaseService.InsertAsync(movieEvent);
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
                await _databaseService.UpsertAsync(movieEvent);
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
                MovieEvent? movieEvent = await GetByIdAsync(id);
                if (movieEvent == null)
                    return false;

                await _databaseService.DeleteAsync<MovieEvent>(id);
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
                IEnumerable<MovieEvent> events = await _databaseService.GetAllAsync<MovieEvent>();
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
                IEnumerable<MovieEvent> events = await _databaseService.GetAllAsync<MovieEvent>();
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