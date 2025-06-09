using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MovieReviewApp.Core.Interfaces;

namespace MovieReviewApp.Core.Services
{
    public abstract class BaseService<T> : IBaseService<T> where T : class
    {
        protected readonly IDatabaseService _databaseService;
        protected readonly ILogger _logger;

        protected BaseService(IDatabaseService databaseService, ILogger logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        public virtual async Task<List<T>> GetAllAsync()
        {
            try
            {
                return await _databaseService.GetAllAsync<T>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all {Type}", typeof(T).Name);
                return new List<T>();
            }
        }

        public virtual async Task<T?> GetByIdAsync(string id)
        {
            try
            {
                return await _databaseService.GetByIdAsync<T>(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get {Type} by id {Id}", typeof(T).Name, id);
                return null;
            }
        }

        public virtual async Task<T> CreateAsync(T entity)
        {
            try
            {
                await _databaseService.InsertAsync(entity);
                _logger.LogInformation("Created {Type}", typeof(T).Name);
                return entity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create {Type}", typeof(T).Name);
                throw;
            }
        }

        public virtual async Task<T> UpdateAsync(T entity)
        {
            try
            {
                await _databaseService.UpsertAsync(entity);
                _logger.LogInformation("Updated {Type}", typeof(T).Name);
                return entity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update {Type}", typeof(T).Name);
                throw;
            }
        }

        public virtual async Task<bool> DeleteAsync(string id)
        {
            try
            {
                T? entity = await GetByIdAsync(id);
                if (entity == null)
                    return false;

                await _databaseService.DeleteAsync<T>(id);
                _logger.LogInformation("Deleted {Type} {Id}", typeof(T).Name, id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete {Type} {Id}", typeof(T).Name, id);
                return false;
            }
        }
    }
} 