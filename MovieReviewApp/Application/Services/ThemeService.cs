using Microsoft.Extensions.Options;
using MovieReviewApp.Models;
using MovieReviewApp.Application.Services;

namespace MovieReviewApp.Application.Services
{
    public class ThemeService
    {
        private readonly MovieReviewService _movieReviewService;
        private readonly AppSettings _appSettings;
        private string _currentTheme = "dark";

        public ThemeService(MovieReviewService movieReviewService, IOptions<AppSettings> appSettings)
        {
            _movieReviewService = movieReviewService;
            _appSettings = appSettings.Value;
        }

        public event Action<string>? ThemeChanged;

        public string CurrentTheme => _currentTheme;

        public async Task InitializeAsync()
        {
            Setting? setting = await _movieReviewService.GetSettingAsync("theme");
            _currentTheme = setting?.Value ?? "dark";
        }

        public async Task<string> GetThemeAsync()
        {
            Setting? setting = await _movieReviewService.GetSettingAsync("theme");
            _currentTheme = setting?.Value ?? "dark";
            return _currentTheme;
        }

        public async Task SetThemeAsync(string theme)
        {
            if (theme != "light" && theme != "dark")
            {
                theme = "dark";
            }

            _currentTheme = theme;

            Setting? setting = await _movieReviewService.GetSettingAsync("theme");
            if (setting == null)
            {
                setting = new Setting
                {
                    Key = "theme",
                    Value = theme,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
            }
            else
            {
                setting.Value = theme;
                setting.UpdatedAt = DateTime.UtcNow;
            }

            await _movieReviewService.AddOrUpdateSettingAsync(setting);
            ThemeChanged?.Invoke(theme);
        }

        public async Task ToggleThemeAsync()
        {
            string newTheme = _currentTheme == "dark" ? "light" : "dark";
            await SetThemeAsync(newTheme);
        }

        public async Task<string> GetGroupThemeAsync()
        {
            Setting? setting = await _movieReviewService.GetSettingAsync("group_theme");
            return setting?.Value ?? "cyberpunk";
        }
    }
} 