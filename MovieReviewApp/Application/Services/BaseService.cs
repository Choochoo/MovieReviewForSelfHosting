using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Base service class providing common CRUD operations for entities
/// </summary>
public abstract class BaseService<T>(MongoDbService databaseService, ILogger logger) where T : BaseModel
{
    protected readonly MongoDbService _db = databaseService;
    protected readonly ILogger _logger = logger;

    public virtual async Task<List<T>> GetAllAsync()
    {
        return await _db.GetAllAsync<T>();
    }

    public virtual async Task<T?> GetByIdAsync(Guid id)
    {
        return await _db.GetByIdAsync<T>(id);
    }

    public virtual async Task<T> CreateAsync(T entity)
    {
        await _db.InsertAsync(entity);
        _logger.LogInformation("Created {Type}", typeof(T).Name);
        entity.UpdatedAt = DateTime.UtcNow;
        return entity;
    }

    public virtual async Task<T> UpdateAsync(T entity)
    {
        await _db.UpsertAsync(entity);
        _logger.LogInformation("Updated {Type}", typeof(T).Name);
        entity.UpdatedAt = DateTime.UtcNow;
        return entity;
    }

    public virtual async Task<T> UpsertAsync(T entity)
    {
        await _db.UpsertAsync(entity);
        _logger.LogInformation("Upserted {Type}", typeof(T).Name);
        entity.UpdatedAt = DateTime.UtcNow;
        return entity;
    }

    public virtual async Task<bool> DeleteAsync(Guid id)
    {
        try
        {
            bool result = await _db.DeleteAsync<T>(id);
            if (result)
            {
                _logger.LogInformation("Deleted {Type} {Id}", typeof(T).Name, id);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete {Type} {Id}: {Message}", typeof(T).Name, id, ex.Message);
            throw;
        }
    }

    public virtual async Task<bool> DeleteAsync(T entity)
    {
        try
        {
            bool result = await _db.DeleteAsync<T>(entity.Id);
            if (result)
            {
                _logger.LogInformation("Deleted {Type} {Id}", typeof(T).Name, entity.Id);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete {Type} {Id}: {Message}", typeof(T).Name, entity.Id, ex.Message);
            throw;
        }
    }
}
