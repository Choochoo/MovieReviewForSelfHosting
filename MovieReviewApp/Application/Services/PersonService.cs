using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Models;
using MongoDB.Driver;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Service responsible for managing people and their lifecycle.
/// Handles CRUD operations and person state management.
/// </summary>
public class PersonService(MongoDbService databaseService, ILogger<PersonService> logger)
    : BaseService<Person>(databaseService, logger)
{
    public async Task<List<Person>> GetAllOrderedAsync(bool respectOrder)
    {
        IMongoCollection<Person>? collection = _db.GetCollection<Person>();
        FilterDefinition<Person> filter = Builders<Person>.Filter.Empty;
        
        if (respectOrder)
        {
            SortDefinition<Person> sort = Builders<Person>.Sort.Ascending(p => p.Order);
            return await collection.Find(filter).Sort(sort).ToListAsync();
        }
        
        return await collection.Find(filter).ToListAsync();
    }
}
