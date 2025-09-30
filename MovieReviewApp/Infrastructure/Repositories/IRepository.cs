using System.Linq.Expressions;
using MovieReviewApp.Models;

namespace MovieReviewApp.Infrastructure.Repositories;

/// <summary>
/// Generic repository interface providing data access abstraction
/// Enforces separation of concerns by isolating database operations from business logic
/// </summary>
public interface IRepository<T> where T : BaseModel
{
    // Read operations
    Task<List<T>> GetAllAsync();
    Task<T?> GetByIdAsync(Guid id);
    Task<List<T>> FindAsync(Expression<Func<T, bool>> filter);
    Task<T?> FindOneAsync(Expression<Func<T, bool>> filter);
    Task<long> CountAsync(Expression<Func<T, bool>>? filter = null);
    Task<bool> AnyAsync(Expression<Func<T, bool>>? filter = null);
    Task<List<T>> GetByDateRangeAsync(DateTime start, DateTime end, string dateFieldName = "StartDate");

    // Write operations
    Task<T> CreateAsync(T entity);
    Task<T> UpdateAsync(T entity);
    Task<T> UpsertAsync(T entity);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> DeleteAsync(T entity);
    Task<long> DeleteManyAsync(Expression<Func<T, bool>> filter);

    // Bulk operations
    Task InsertManyAsync(IEnumerable<T> documents);

    // Pagination
    Task<(List<T> items, long totalCount)> GetPagedAsync(
        int page,
        int pageSize,
        Expression<Func<T, bool>>? filter = null,
        Expression<Func<T, object>>? orderBy = null,
        bool descending = false);

    // Search
    Task<List<T>> SearchTextAsync(string searchTerm, params Expression<Func<T, object>>[] searchFields);
}