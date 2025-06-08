using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace MovieReviewApp.Core.Interfaces
{
    public interface IDatabaseService
    {
        bool IsConnected { get; }
        
        Task<List<T>> GetAllAsync<T>() where T : class;
        Task<T?> GetByIdAsync<T>(string id) where T : class;
        Task<T?> GetByIdAsync<T>(object id) where T : class;
        Task<List<T>> FindAsync<T>(Expression<Func<T, bool>> filter) where T : class;
        Task<T?> FindOneAsync<T>(Expression<Func<T, bool>> filter) where T : class;
        Task InsertAsync<T>(T entity) where T : class;
        Task UpsertAsync<T>(T entity) where T : class;
        Task DeleteAsync<T>(string id) where T : class;
        Task<bool> DeleteByIdAsync<T>(Guid id) where T : class;
        Task<long> CountAsync<T>(Expression<Func<T, bool>>? filter = null) where T : class;
        Task<bool> AnyAsync<T>(Expression<Func<T, bool>>? filter = null) where T : class;
        Task<List<T>> SearchTextAsync<T>(string searchTerm, params Expression<Func<T, object>>[] searchFields) where T : class;
    }
} 