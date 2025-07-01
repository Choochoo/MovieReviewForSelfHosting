using MovieReviewApp.Models;
using MovieReviewApp.Utilities;
using MovieReviewApp.Infrastructure.Database;
using MongoDB.Driver;

namespace MovieReviewApp.Application.Services;

public class CurrentEventService(MongoDbService databaseService) : ICurrentEventService
{
    public async Task<MovieEvent?> GetCurrentEventAsync()
    {
        DateTime now = DateProvider.Now;
        
        IMongoCollection<MovieEvent>? collection = databaseService.GetCollection<MovieEvent>();
        if (collection == null) return null;

        return await collection
            .Find(e => e.StartDate <= now && e.EndDate >= now)
            .SortByDescending(e => e.StartDate)
            .FirstOrDefaultAsync();
    }

    public async Task<MovieEvent?> GetNextEventAsync()
    {
        DateTime now = DateProvider.Now;
        
        IMongoCollection<MovieEvent>? collection = databaseService.GetCollection<MovieEvent>();
        if (collection == null) return null;

        return await collection
            .Find(e => e.StartDate > now)
            .SortBy(e => e.StartDate)
            .FirstOrDefaultAsync();
    }

    public async Task<MovieEvent?> GetMostRecentPastEventAsync()
    {
        DateTime now = DateProvider.Now;
        
        IMongoCollection<MovieEvent>? collection = databaseService.GetCollection<MovieEvent>();
        if (collection == null) return null;

        return await collection
            .Find(e => e.EndDate < now)
            .SortByDescending(e => e.StartDate)
            .FirstOrDefaultAsync();
    }
}