using System.Linq.Expressions;
using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Models;

namespace MovieReviewApp.Infrastructure.Repositories;

/// <summary>
/// MongoDB implementation of the repository pattern
/// Provides concrete data access operations while maintaining separation of concerns
/// </summary>
public class MongoRepository<T> : IRepository<T> where T : BaseModel
{
    private readonly MongoDbService _db;
    private readonly ILogger<MongoRepository<T>> _logger;

    public MongoRepository(MongoDbService db, ILogger<MongoRepository<T>> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<T>> GetAllAsync()
    {
        return await _db.GetAllAsync<T>();
    }

    public async Task<T?> GetByIdAsync(Guid id)
    {
        return await _db.GetByIdAsync<T>(id);
    }

    public async Task<List<T>> FindAsync(Expression<Func<T, bool>> filter)
    {
        return await _db.FindAsync(filter);
    }

    public async Task<T?> FindOneAsync(Expression<Func<T, bool>> filter)
    {
        return await _db.FindOneAsync(filter);
    }

    public async Task<long> CountAsync(Expression<Func<T, bool>>? filter = null)
    {
        return await _db.CountAsync(filter);
    }

    public async Task<bool> AnyAsync(Expression<Func<T, bool>>? filter = null)
    {
        return await _db.AnyAsync(filter);
    }

    public async Task<List<T>> GetByDateRangeAsync(DateTime start, DateTime end, string dateFieldName = "StartDate")
    {
        return await _db.GetByDateRangeAsync<T>(start, end, dateFieldName);
    }

    public async Task<T> CreateAsync(T entity)
    {
        await _db.InsertAsync(entity);
        _logger.LogInformation("Created {Type} with ID {Id}", typeof(T).Name, entity.Id);
        entity.UpdatedAt = DateTime.UtcNow;
        return entity;
    }

    public async Task<T> UpdateAsync(T entity)
    {
        await _db.UpsertAsync(entity);
        _logger.LogInformation("Updated {Type} with ID {Id}", typeof(T).Name, entity.Id);
        entity.UpdatedAt = DateTime.UtcNow;
        return entity;
    }

    public async Task<T> UpsertAsync(T entity)
    {
        await _db.UpsertAsync(entity);
        _logger.LogInformation("Upserted {Type} with ID {Id}", typeof(T).Name, entity.Id);
        entity.UpdatedAt = DateTime.UtcNow;
        return entity;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        bool result = await _db.DeleteAsync<T>(id);
        if (result)
        {
            _logger.LogInformation("Deleted {Type} with ID {Id}", typeof(T).Name, id);
        }
        return result;
    }

    public async Task<bool> DeleteAsync(T entity)
    {
        bool result = await _db.DeleteAsync<T>(entity.Id);
        if (result)
        {
            _logger.LogInformation("Deleted {Type} with ID {Id}", typeof(T).Name, entity.Id);
        }
        return result;
    }

    public async Task<long> DeleteManyAsync(Expression<Func<T, bool>> filter)
    {
        long deletedCount = await _db.DeleteManyAsync(filter);
        _logger.LogInformation("Deleted {Count} records of {Type}", deletedCount, typeof(T).Name);
        return deletedCount;
    }

    public async Task InsertManyAsync(IEnumerable<T> documents)
    {
        await _db.InsertManyAsync(documents);
        _logger.LogInformation("Inserted {Count} records of {Type}", documents.Count(), typeof(T).Name);
    }

    public async Task<(List<T> items, long totalCount)> GetPagedAsync(
        int page,
        int pageSize,
        Expression<Func<T, bool>>? filter = null,
        Expression<Func<T, object>>? orderBy = null,
        bool descending = false)
    {
        return await _db.GetPagedAsync(page, pageSize, filter, orderBy, descending);
    }

    public async Task<List<T>> SearchTextAsync(string searchTerm, params Expression<Func<T, object>>[] searchFields)
    {
        return await _db.SearchTextAsync(searchTerm, searchFields);
    }
}