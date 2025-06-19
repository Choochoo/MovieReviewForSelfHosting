
using System.Text.Json;
using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Infrastructure.Services;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

public class SettingService(MongoDbService databaseService, ILogger<SettingService> logger, DemoProtectionService demoProtectionService)
    : BaseService<Setting>(databaseService, logger)
{

    public async Task AddOrUpdateSettingAsync(Setting setting)
    {
        if (demoProtectionService.IsDemoInstance)
        {
            // In demo mode, silently ignore writes to mimic database behavior
            // but don't actually persist anything
            logger.LogInformation("Demo mode: Ignoring setting update for key {Key}", setting.Key);
            return;
        }

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

    private async Task AddOrUpdateSettingInternalAsync(Setting setting)
    {
        // Internal method for non-demo instances
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

    public async Task<ApplicationSettings> GetApplicationSettingsAsync()
    {
        Setting? appSettingEntry = await GetSettingAsync("ApplicationSettings");

        if (appSettingEntry != null && !string.IsNullOrEmpty(appSettingEntry.Value))
        {
            return JsonSerializer.Deserialize<ApplicationSettings>(appSettingEntry.Value)
                ?? await CreateDefaultApplicationSettingsAsync();
        }

        return await CreateDefaultApplicationSettingsAsync();
    }

    private async Task<ApplicationSettings> CreateDefaultApplicationSettingsAsync()
    {
        logger.LogInformation("Creating default ApplicationSettings...");
        ApplicationSettings defaultSettings = new ApplicationSettings();

        // Set demo-specific defaults if in demo mode
        if (demoProtectionService.IsDemoInstance)
        {
            defaultSettings.IsDemoMode = true;
            defaultSettings.AllowDemoDataModification = false;
            defaultSettings.DefaultTheme = "cyberpunk";
        }

        string serializedSettings = JsonSerializer.Serialize(defaultSettings);

        Setting appSettingEntry = new Setting
        {
            Key = "ApplicationSettings",
            Value = serializedSettings,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        logger.LogInformation("Created default ApplicationSettings");
        return defaultSettings;
    }

    public async Task<AwardSetting> GetAwardSettingsAsync()
    {
        Setting? awardSettingEntry = await GetSettingAsync("AwardSettings");

        if (awardSettingEntry != null && !string.IsNullOrEmpty(awardSettingEntry.Value))
        {
            return JsonSerializer.Deserialize<AwardSetting>(awardSettingEntry.Value)
                ?? await CreateDefaultAwardSettingAsync();
        }

        return await CreateDefaultAwardSettingAsync();
    }

    private async Task<AwardSetting> CreateDefaultAwardSettingAsync()
    {
        ApplicationSettings appSettings = await GetApplicationSettingsAsync();
        AwardSetting defaultAwardSetting = AwardSetting.CreateFromApplicationSettings(appSettings);
        string serializedSettings = JsonSerializer.Serialize(defaultAwardSetting);

        Setting awardSettingEntry = new Setting
        {
            Key = "AwardSettings",
            Value = serializedSettings,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        return defaultAwardSetting;
    }

    public async Task UpdateApplicationSettingsToLatestAsync()
    {
        logger.LogInformation("Updating ApplicationSettings to latest version...");
        ApplicationSettings latestSettings = new ApplicationSettings();
        
        // Set demo-specific defaults if in demo mode
        if (demoProtectionService.IsDemoInstance)
        {
            latestSettings.IsDemoMode = true;
            latestSettings.AllowDemoDataModification = false;
            latestSettings.DefaultTheme = "cyberpunk";
        }

        string serializedSettings = JsonSerializer.Serialize(latestSettings);

        Setting appSettingEntry = new Setting
        {
            Key = "ApplicationSettings",
            Value = serializedSettings,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await AddOrUpdateSettingInternalAsync(appSettingEntry);
        logger.LogInformation("Updated ApplicationSettings with latest themes");
    }

    public async Task<string> GetSettingValueAsync(string key, string defaultValue = "")
    {
        Setting? setting = await GetSettingAsync(key);
        if (setting == null && !string.IsNullOrEmpty(defaultValue))
        {
            await CreateDefaultGeneralSettingAsync(key, defaultValue);
            return defaultValue;
        }
        return setting?.Value ?? defaultValue;
    }

    public async Task CreateDefaultGeneralSettingsAsync()
    {
        logger.LogInformation("Creating default general settings...");

        if (!_db.IsConnected)
        {
            logger.LogWarning("Database not connected - cannot create settings");
            return;
        }

        logger.LogInformation("Finished creating default general settings");
    }

    private async Task CreateDefaultGeneralSettingAsync(string key, string defaultValue)
    {
        Setting? existingSetting = await GetSettingAsync(key);
        if (existingSetting == null)
        {
            logger.LogInformation("Creating default setting: {Key} = {Value}", key, defaultValue);
            Setting newSetting = new Setting
            {
                Key = key,
                Value = defaultValue,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            // Use UpsertAsync since we're in initial setup mode which allows it
            await UpsertAsync(newSetting);
            logger.LogInformation("Created default setting: {Key}", key);
        }
        else
        {
            logger.LogInformation("Setting {Key} already exists with value: {Value}", key, existingSetting.Value);
        }
    }
}
