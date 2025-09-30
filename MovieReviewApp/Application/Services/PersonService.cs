using MovieReviewApp.Infrastructure.Repositories;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Service responsible for managing people and their lifecycle.
/// Handles CRUD operations and person state management.
/// </summary>
public class PersonService(IRepository<Person> repository, ILogger<PersonService> logger)
    : BaseService<Person>(repository, logger)
{
    public async Task<List<Person>> GetAllOrderedAsync(bool respectOrder)
    {
        List<Person> allPeople = await _repository.GetAllAsync();

        if (respectOrder)
        {
            return allPeople.OrderBy(p => p.Order).ToList();
        }

        return allPeople;
    }
}
