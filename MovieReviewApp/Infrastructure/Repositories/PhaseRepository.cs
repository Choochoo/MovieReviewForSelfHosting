using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Models;

namespace MovieReviewApp.Infrastructure.Repositories
{
    public class PhaseRepository
    {
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<PhaseRepository> _logger;
        private readonly MovieEventRepository _movieEventRepository;

        public PhaseRepository(
            IDatabaseService databaseService,
            ILogger<PhaseRepository> logger,
            MovieEventRepository movieEventRepository)
        {
            _databaseService = databaseService;
            _logger = logger;
            _movieEventRepository = movieEventRepository;
        }

        public async Task<List<Phase>> GetAllAsync()
        {
            try
            {
                IEnumerable<Phase> phases = await _databaseService.GetAllAsync<Phase>();
                foreach (var phase in phases)
                {
                    phase.Events = await _movieEventRepository.GetByPhaseAsync(phase.Number);
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
                Phase? phase = await _databaseService.GetByIdAsync<Phase>(id);
                if (phase != null)
                {
                    phase.Events = await _movieEventRepository.GetByPhaseAsync(phase.Number);
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
                IEnumerable<Phase> phases = await _databaseService.GetAllAsync<Phase>();
                Phase? phase = phases.FirstOrDefault(p => p.Number == number);
                if (phase != null)
                {
                    phase.Events = await _movieEventRepository.GetByPhaseAsync(phase.Number);
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
                await _databaseService.InsertAsync(phase);
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
                await _databaseService.UpsertAsync(phase);
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
                Phase? phase = await GetByIdAsync(id);
                if (phase == null)
                    return false;

                await _databaseService.DeleteAsync<Phase>(id);
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
                IEnumerable<Phase> phases = await _databaseService.GetAllAsync<Phase>();
                Phase? currentPhase = phases
                    .Where(p => p.StartDate <= DateTime.UtcNow && p.EndDate >= DateTime.UtcNow)
                    .OrderByDescending(p => p.Number)
                    .FirstOrDefault();

                if (currentPhase != null)
                {
                    currentPhase.Events = await _movieEventRepository.GetByPhaseAsync(currentPhase.Number);
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