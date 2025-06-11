
using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Models;
using System.Text.Json;

namespace MovieReviewApp.Application.Services;

public class SettingService(MongoDbService databaseService, ILogger<SettingService> logger)
    : BaseService<Setting>(databaseService, logger)
{


    public async Task AddOrUpdateSettingAsync(Setting setting)
    {
        List<Setting> settings = await GetAllAsync();
        Setting existing = settings.FirstOrDefault(s => s.Key == setting.Key);
        if (existing != null)
        {
            setting.Id = existing.Id;
        }
        _ = await UpdateAsync(setting);
        // Remove duplicates if any exist
        List<Setting> duplicates = settings.Where(s => s.Key == setting.Key).ToList();
        if (duplicates.Count > 1)
        {
            foreach (Setting duplicate in duplicates.Skip(1))
            {
                _ = await DeleteAsync(duplicate.Id);
            }
        }
    }

    public async Task<Setting?> GetSettingAsync(string key)
    {
        List<Setting> settings = await GetAllAsync();
        return settings.FirstOrDefault(s => s.Key == key);
    }

    public async Task<AwardSetting> GetAwardSettingsAsync()
    {
        List<Setting> settings = await GetAllAsync();
        Setting? awardSettingEntry = settings.FirstOrDefault(s => s.Key == "AwardSettings");
        
        if (awardSettingEntry != null && !string.IsNullOrEmpty(awardSettingEntry.Value))
        {
            return JsonSerializer.Deserialize<AwardSetting>(awardSettingEntry.Value) 
                ?? new AwardSetting();
        }
        
        return new AwardSetting();
    }
}
