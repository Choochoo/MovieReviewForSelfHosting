using MovieReviewApp.Infrastructure.Repositories;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Base service class providing common CRUD operations for entities
/// Uses repository pattern to maintain proper separation of concerns
/// </summary>
public abstract class BaseService<T>(IRepository<T> repository, ILogger logger) where T : BaseModel
{
    protected readonly IRepository<T> _repository = repository;
    protected readonly ILogger _logger = logger;

    public virtual async Task<List<T>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public virtual async Task<T?> GetByIdAsync(Guid id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public virtual async Task<List<T>> GetByDateRangeAsync(DateTime start, DateTime end, string dateFieldName = "StartDate")
    {
        return await _repository.GetByDateRangeAsync(start, end, dateFieldName);
    }

    public virtual async Task<long> GetCountAsync()
    {
        return await _repository.CountAsync();
    }

    public virtual async Task<T> CreateAsync(T entity)
    {
        return await _repository.CreateAsync(entity);
    }

    public virtual async Task<T> UpdateAsync(T entity)
    {
        return await _repository.UpdateAsync(entity);
    }

    public virtual async Task<T> UpsertAsync(T entity)
    {
        return await _repository.UpsertAsync(entity);
    }

    public virtual async Task<bool> DeleteAsync(Guid id)
    {
        try
        {
            return await _repository.DeleteAsync(id);
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
            return await _repository.DeleteAsync(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete {Type} {Id}: {Message}", typeof(T).Name, entity.Id, ex.Message);
            throw;
        }
    }
}
