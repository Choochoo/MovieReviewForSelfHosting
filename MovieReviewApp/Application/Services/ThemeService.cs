using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using MovieReviewApp.Models;
using MovieReviewApp.Application.Services;
using MovieReviewApp.Infrastructure.Services;

namespace MovieReviewApp.Application.Services
{
    public class ThemeService
    {
        private readonly SettingService _settingService;
        private readonly AppSettings _appSettings;
        private readonly DemoProtectionService _demoProtection;
        private readonly IJSRuntime _jsRuntime;
        private string _currentGroupTheme = "cyberpunk";
        private bool _isDarkMode = false;
        private bool _initialized = false;

        public ThemeService(SettingService settingService, IOptions<AppSettings> appSettings, DemoProtectionService demoProtection, IJSRuntime jsRuntime)
        {
            _settingService = settingService;
            _appSettings = appSettings.Value;
            _demoProtection = demoProtection;
            _jsRuntime = jsRuntime;
        }

        public event Action<string, bool>? ThemeChanged;

        public string CurrentGroupTheme => _currentGroupTheme;
        public bool IsDarkMode => _isDarkMode;

        public async Task InitializeAsync()
        {
            if (_initialized) return;

            // Load group theme from database
            Setting? groupThemeSetting = await _settingService.GetSettingAsync("group_theme");
            _currentGroupTheme = groupThemeSetting?.Value ?? "cyberpunk";

            // Load dark mode from localStorage
            try
            {
                _isDarkMode = await _jsRuntime.InvokeAsync<bool>("getDarkMode");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load dark mode from localStorage: {ex.Message}");
                // Default to light mode for demo instances, dark for normal instances
                _isDarkMode = !_demoProtection.IsDemoInstance;
            }

            _initialized = true;
        }

        public async Task<string> GetGroupThemeAsync()
        {
            Setting? setting = await _settingService.GetSettingAsync("group_theme");
            _currentGroupTheme = setting?.Value ?? "cyberpunk";
            return _currentGroupTheme;
        }

        public async Task SetGroupThemeAsync(string groupTheme)
        {
            string[] validThemes = { "cyberpunk", "ocean", "nature", "classic" };
            if (!validThemes.Contains(groupTheme))
            {
                groupTheme = "cyberpunk";
            }

            _currentGroupTheme = groupTheme;

            // Try to save to database
            try
            {
                Setting? setting = await _settingService.GetSettingAsync("group_theme");
                if (setting == null)
                {
                    setting = new Setting
                    {
                        Key = "group_theme",
                        Value = groupTheme,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                }
                else
                {
                    setting.Value = groupTheme;
                    setting.UpdatedAt = DateTime.UtcNow;
                }

                await _settingService.AddOrUpdateSettingAsync(setting);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save group theme to database: {ex.Message}");
            }

            // Trigger theme change event
            ThemeChanged?.Invoke(_currentGroupTheme, _isDarkMode);
        }

        public async Task SetDarkMode(bool isDark)
        {
            _isDarkMode = isDark;
            
            // Save to localStorage via JavaScript
            try
            {
                await _jsRuntime.InvokeVoidAsync("setDarkMode", isDark);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save dark mode to localStorage: {ex.Message}");
            }
            
            ThemeChanged?.Invoke(_currentGroupTheme, _isDarkMode);
        }

        public async Task ToggleDarkMode()
        {
            await SetDarkMode(!_isDarkMode);
        }

        public string GetCombinedTheme()
        {
            return $"{_currentGroupTheme}-{(_isDarkMode ? "dark" : "light")}";
        }

        // Legacy methods for backward compatibility
        [Obsolete("Use GetCombinedTheme() instead")]
        public async Task<string> GetThemeAsync()
        {
            await GetGroupThemeAsync();
            return GetCombinedTheme();
        }

        [Obsolete("Use SetGroupThemeAsync() and SetDarkMode() instead")]
        public async Task SetThemeAsync(string theme)
        {
            if (theme == "light")
            {
                await SetDarkMode(false);
            }
            else if (theme == "dark")
            {
                await SetDarkMode(true);
            }
        }

        [Obsolete("Use ToggleDarkMode() instead")]
        public async Task ToggleThemeAsync()
        {
            await ToggleDarkMode();
        }
    }
} 