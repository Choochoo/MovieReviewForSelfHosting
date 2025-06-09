using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Models;

namespace MovieReviewApp.Infrastructure.Repositories
{
    public class SettingRepository
    {
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<SettingRepository> _logger;

        public SettingRepository(
            IDatabaseService databaseService,
            ILogger<SettingRepository> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        public async Task<List<Setting>> GetAllAsync()
        {
            try
            {
                return await _databaseService.GetAllAsync<Setting>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all settings");
                return new List<Setting>();
            }
        }

        public async Task<Setting?> GetByIdAsync(string id)
        {
            try
            {
                return await _databaseService.GetByIdAsync<Setting>(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get setting by id {Id}", id);
                return null;
            }
        }

        public async Task<Setting?> GetByKeyAsync(string key)
        {
            try
            {
                IEnumerable<Setting> settings = await _databaseService.GetAllAsync<Setting>();
                return settings.FirstOrDefault(s => s.Key == key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get setting by key {Key}", key);
                return null;
            }
        }

        public async Task<Setting> CreateAsync(Setting setting)
        {
            try
            {
                setting.CreatedAt = DateTime.UtcNow;
                setting.UpdatedAt = DateTime.UtcNow;
                await _databaseService.InsertAsync(setting);
                _logger.LogInformation("Created setting {Key}", setting.Key);
                return setting;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create setting {Key}", setting.Key);
                throw;
            }
        }

        public async Task<Setting> UpdateAsync(Setting setting)
        {
            try
            {
                setting.UpdatedAt = DateTime.UtcNow;
                await _databaseService.UpsertAsync(setting);
                _logger.LogInformation("Updated setting {Key}", setting.Key);
                return setting;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update setting {Key}", setting.Key);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string id)
        {
            try
            {
                Setting? setting = await GetByIdAsync(id);
                if (setting == null)
                    return false;

                await _databaseService.DeleteAsync<Setting>(id);
                _logger.LogInformation("Deleted setting {Id}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete setting {Id}", id);
                return false;
            }
        }

        public async Task<string?> GetValueAsync(string key)
        {
            try
            {
                Setting? setting = await GetByKeyAsync(key);
                return setting?.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get value for setting {Key}", key);
                return null;
            }
        }

        public async Task<bool> SetValueAsync(string key, string value)
        {
            try
            {
                Setting? setting = await GetByKeyAsync(key);
                if (setting == null)
                {
                    setting = new Setting
                    {
                        Key = key,
                        Value = value,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    await _databaseService.InsertAsync(setting);
                }
                else
                {
                    setting.Value = value;
                    setting.UpdatedAt = DateTime.UtcNow;
                    await _databaseService.UpsertAsync(setting);
                }
                _logger.LogInformation("Set value for setting {Key}", key);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set value for setting {Key}", key);
                return false;
            }
        }
    }
} 