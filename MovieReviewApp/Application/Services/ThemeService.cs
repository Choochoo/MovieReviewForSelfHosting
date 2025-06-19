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

            // Load group theme from database or application settings
            ApplicationSettings appSettings = await _settingService.GetApplicationSettingsAsync();
            Setting? groupThemeSetting = await _settingService.GetSettingAsync("group_theme");
            _currentGroupTheme = groupThemeSetting?.Value ?? appSettings.DefaultTheme;

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

            // Apply the initial theme to the DOM
            try
            {
                await _jsRuntime.InvokeVoidAsync("setGroupThemeAttribute", _currentGroupTheme);
                await _jsRuntime.InvokeVoidAsync("setTheme", _currentGroupTheme, _isDarkMode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize theme in DOM: {ex.Message}");
            }

            _initialized = true;
        }

        public async Task<string> GetGroupThemeAsync()
        {
            ApplicationSettings appSettings = await _settingService.GetApplicationSettingsAsync();
            Setting? setting = await _settingService.GetSettingAsync("group_theme");
            _currentGroupTheme = setting?.Value ?? appSettings.DefaultTheme;
            return _currentGroupTheme;
        }

        public async Task SetGroupThemeAsync(string groupTheme)
        {
            // Get valid themes from application settings
            ApplicationSettings appSettings = await _settingService.GetApplicationSettingsAsync();
            if (!appSettings.AvailableThemes.Contains(groupTheme))
            {
                groupTheme = appSettings.DefaultTheme;
            }

            _currentGroupTheme = groupTheme;

            // Only save to database if not in demo mode
            if (!_demoProtection.IsDemoInstance)
            {
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
            }

            // Update the DOM with the new group theme
            try
            {
                await _jsRuntime.InvokeVoidAsync("setGroupThemeAttribute", groupTheme);
                await _jsRuntime.InvokeVoidAsync("setTheme", groupTheme, _isDarkMode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to apply group theme to DOM: {ex.Message}");
            }

            // Trigger theme change event
            ThemeChanged?.Invoke(_currentGroupTheme, _isDarkMode);
        }

        public async Task SetDarkMode(bool isDark)
        {
            _isDarkMode = isDark;
            
            // Ensure group theme attribute is set before applying dark mode
            try
            {
                // First make sure the group theme is set in DOM
                await _jsRuntime.InvokeVoidAsync("setGroupThemeAttribute", _currentGroupTheme);
                // Then apply the combined theme directly
                await _jsRuntime.InvokeVoidAsync("setTheme", _currentGroupTheme, isDark);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to apply dark mode: {ex.Message}");
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