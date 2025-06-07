using Microsoft.Extensions.Options;
using MovieReviewApp.Models;
using MovieReviewApp.Services;

namespace MovieReviewApp.Services
{
    public class ThemeService
    {
        private readonly MovieReviewService _movieReviewService;
        private readonly AppSettings _appSettings;

        public ThemeService(MovieReviewService movieReviewService, IOptions<AppSettings> appSettings)
        {
            _movieReviewService = movieReviewService;
            _appSettings = appSettings.Value;
        }

        public async Task<string> GetGroupThemeAsync()
        {
            var setting = await _movieReviewService.GetSettingAsync("theme");
            return setting?.Value ?? "cyberpunk";
        }

        public async Task SaveGroupThemeAsync(string theme)
        {
            var setting = new Setting
            {
                Key = "theme",
                Value = theme,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _movieReviewService.AddOrUpdateSettingAsync(setting);
        }

        public async Task<string> GetSettingAsync(string groupId, string key)
        {
            var setting = await _movieReviewService.GetSettingAsync(key);
            return setting?.Value;
        }

        public async Task SaveSettingAsync(string groupId, string key, string value)
        {
            var setting = await _movieReviewService.GetSettingAsync(key);
            if (setting == null)
            {
                setting = new Setting
                {
                    Key = key,
                    Value = value,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _movieReviewService.AddOrUpdateSettingAsync(setting);
            }
            else
            {
                setting.Value = value;
                setting.UpdatedAt = DateTime.UtcNow;
                await _movieReviewService.AddOrUpdateSettingAsync(setting);
            }
        }
    }
} 