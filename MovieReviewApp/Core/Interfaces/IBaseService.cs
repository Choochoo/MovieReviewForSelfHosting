using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MovieReviewApp.Core.Interfaces
{
    public interface IBaseService<T> where T : class
    {
        Task<List<T>> GetAllAsync();
        Task<T?> GetByIdAsync(string id);
        Task<T> CreateAsync(T entity);
        Task<T> UpdateAsync(T entity);
        Task<bool> DeleteAsync(string id);
    }
} 