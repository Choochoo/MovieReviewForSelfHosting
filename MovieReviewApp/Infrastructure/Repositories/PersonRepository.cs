using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Models;

namespace MovieReviewApp.Infrastructure.Repositories
{
    public class PersonRepository
    {
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<PersonRepository> _logger;

        public PersonRepository(
            IDatabaseService databaseService,
            ILogger<PersonRepository> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        public async Task<List<Person>> GetAllAsync()
        {
            try
            {
                IEnumerable<Person> people = await _databaseService.GetAllAsync<Person>();
                return people.OrderBy(p => p.Order).ThenBy(p => p.Name).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get people");
                return new List<Person>();
            }
        }

        public async Task<Person?> GetByIdAsync(string id)
        {
            try
            {
                return await _databaseService.GetByIdAsync<Person>(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get person by id {Id}", id);
                return null;
            }
        }

        public async Task<Person> CreateAsync(Person person)
        {
            try
            {
                await _databaseService.InsertAsync(person);
                _logger.LogInformation("Created person {Name}", person.Name);
                return person;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create person {Name}", person.Name);
                throw;
            }
        }

        public async Task<Person> UpdateAsync(Person person)
        {
            try
            {
                person.UpdatedAt = DateTime.UtcNow;
                await _databaseService.UpsertAsync(person);
                _logger.LogInformation("Updated person {Name}", person.Name);
                return person;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update person {Name}", person.Name);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string id)
        {
            try
            {
                Person? person = await GetByIdAsync(id);
                if (person == null)
                    return false;

                await _databaseService.DeleteAsync<Person>(id);
                _logger.LogInformation("Deleted person {Id}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete person {Id}", id);
                return false;
            }
        }
    }
} 