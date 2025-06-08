using MovieReviewApp.Database;
using MovieReviewApp.Models;

namespace MovieReviewApp.Services
{
    public class PhaseService
    {
        private readonly MongoDbService _mongoDbService;
        private readonly ILogger<PhaseService> _logger;
        private readonly MovieEventService _movieEventService;

        public PhaseService(
            MongoDbService mongoDbService,
            ILogger<PhaseService> logger,
            MovieEventService movieEventService)
        {
            _mongoDbService = mongoDbService;
            _logger = logger;
            _movieEventService = movieEventService;
        }

        public async Task<List<Phase>> GetAllAsync()
        {
            try
            {
                var phases = await _mongoDbService.GetAllAsync<Phase>();
                foreach (var phase in phases)
                {
                    phase.Events = await _movieEventService.GetByPhaseAsync(phase.Number);
                }
                return phases.OrderBy(p => p.Number).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all phases");
                return new List<Phase>();
            }
        }

        public async Task<Phase?> GetByIdAsync(string id)
        {
            try
            {
                var phase = await _mongoDbService.GetByIdAsync<Phase>(id);
                if (phase != null)
                {
                    phase.Events = await _movieEventService.GetByPhaseAsync(phase.Number);
                }
                return phase;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get phase by id {Id}", id);
                return null;
            }
        }

        public async Task<Phase?> GetByNumberAsync(int number)
        {
            try
            {
                var phases = await _mongoDbService.GetAllAsync<Phase>();
                var phase = phases.FirstOrDefault(p => p.Number == number);
                if (phase != null)
                {
                    phase.Events = await _movieEventService.GetByPhaseAsync(phase.Number);
                }
                return phase;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get phase by number {Number}", number);
                return null;
            }
        }

        public async Task<Phase> CreateAsync(Phase phase)
        {
            try
            {
                await _mongoDbService.InsertAsync(phase);
                _logger.LogInformation("Created phase {Number}", phase.Number);
                return phase;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create phase {Number}", phase.Number);
                throw;
            }
        }

        public async Task<Phase> UpdateAsync(Phase phase)
        {
            try
            {
                await _mongoDbService.UpsertAsync(phase);
                _logger.LogInformation("Updated phase {Number}", phase.Number);
                return phase;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update phase {Number}", phase.Number);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string id)
        {
            try
            {
                var phase = await GetByIdAsync(id);
                if (phase == null)
                    return false;

                await _mongoDbService.DeleteAsync<Phase>(id);
                _logger.LogInformation("Deleted phase {Id}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete phase {Id}", id);
                return false;
            }
        }

        public async Task<Phase?> GetCurrentPhaseAsync()
        {
            try
            {
                var phases = await _mongoDbService.GetAllAsync<Phase>();
                var currentPhase = phases
                    .Where(p => p.StartDate <= DateTime.UtcNow && p.EndDate >= DateTime.UtcNow)
                    .OrderByDescending(p => p.Number)
                    .FirstOrDefault();

                if (currentPhase != null)
                {
                    currentPhase.Events = await _movieEventService.GetByPhaseAsync(currentPhase.Number);
                }

                return currentPhase;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get current phase");
                return null;
            }
        }
    }
} 