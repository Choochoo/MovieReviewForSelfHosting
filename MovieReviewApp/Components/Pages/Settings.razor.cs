// Settings.razor.cs
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc.Rendering;
using MovieReviewApp.Application.Services;
using MovieReviewApp.Application.Services.Analysis;
using MovieReviewApp.Infrastructure.Configuration;
using MovieReviewApp.Infrastructure.Services;
using MovieReviewApp.Models;

namespace MovieReviewApp.Components.Pages
{
    public partial class Settings : ComponentBase
    {
        [Inject]
        private PersonService PersonService { get; set; } = default!;

        [Inject]
        private SettingService SettingService { get; set; } = default!;

        [Inject]
        private InstanceManager instanceManager { get; set; } = default!;

        [Inject]
        private NavigationManager navigationManager { get; set; } = default!;

        [Inject]
        private DiscussionQuestionService discussionQuestionsService { get; set; } = default!;

        [Inject]
        private ThemeService themeService { get; set; } = default!;
        
        [Inject]
        private DemoProtectionService demoProtection { get; set; } = default!;

        [Inject]
        private SecretsManager secretsManager { get; set; } = default!;

        [Inject]
        private OpenAIApiService openAIService { get; set; } = default!;

        [Inject]
        private ClaudeService claudeService { get; set; } = default!;

        private List<Person> People { get; set; } = new();
        private DateTime StartDate { get; set; } = DateTime.Now;
        public int TimeCount { get; set; } = 1;
        public string TimePeriod { get; set; } = "Month";
        public string NewPerson { get; set; } = "";
        public bool RespectOrder { get; set; } = false;
        public string GroupName { get; set; } = "";
        public List<Setting> settings { get; set; } = new List<Setting>();
        public string SelectedGroupTheme { get; set; } = "cyberpunk";
        public bool IsDarkMode { get; set; } = false;

        private List<DiscussionQuestion> DiscussionQuestions { get; set; } = new();
        public string NewQuestionText { get; set; } = "";
        public bool NewQuestionIsActive { get; set; } = true;
        
        // Available themes list loaded from ApplicationSettings
        private List<string> AvailableThemes { get; set; } = new();

        // Advanced Settings
        private bool showAdvanced = false;
        private bool showGeneral = true;
        private bool showPeople = false;
        private bool showAwards = false;
        private bool showQuestions = false;
        private string openAIApiKey = "";
        private string claudeApiKey = "";
        private string tmdbApiKey = "";
        private string gladiaApiKey = "";
        private bool showOpenAIKey = false;
        private bool showClaudeKey = false;
        private bool showTMDBKey = false;
        private bool showGladiaKey = false;
        private string apiMessage = "";
        private bool apiMessageIsError = false;

        private bool IsOpenAIConfigured => openAIService?.IsConfigured ?? false;
        private bool IsClaudeConfigured => claudeService?.IsConfigured ?? false;
        private bool IsTMDBConfigured => !string.IsNullOrEmpty(secretsManager?.GetSecret("TMDB:ApiKey"));
        private bool IsGladiaConfigured => !string.IsNullOrEmpty(secretsManager?.GetSecret("Gladia:ApiKey"));

        public readonly List<SelectListItem> TimePeriods = new List<SelectListItem>
        {
            new SelectListItem { Value = "Month", Text = "Month" },
            new SelectListItem { Value = "Week", Text = "Week" },
            new SelectListItem { Value = "Day", Text = "Day" }
        };

        protected override async Task OnInitializedAsync()
        {
            // Ensure default settings exist
            await SettingService.CreateDefaultGeneralSettingsAsync();
            
            // Update ApplicationSettings to latest version (includes new themes)
            await SettingService.UpdateApplicationSettingsToLatestAsync();
            
            // Load settings
            settings = await SettingService.GetAllAsync();

            // Parse settings - they should all exist now
            Setting? respectOrderSetting = settings.FirstOrDefault(x => x.Key == "RespectOrder");
            RespectOrder = respectOrderSetting != null && bool.TryParse(respectOrderSetting.Value, out bool respectOrderValue) && respectOrderValue;

            Setting? startDateSetting = settings.FirstOrDefault(x => x.Key == "StartDate");
            if (startDateSetting != null && DateTime.TryParse(startDateSetting.Value, out DateTime parsedDate))
                StartDate = parsedDate;
            else
                StartDate = DateTime.Now;

            Setting? timeCountSetting = settings.FirstOrDefault(x => x.Key == "TimeCount");
            if (timeCountSetting != null && int.TryParse(timeCountSetting.Value, out int parsedCount))
                TimeCount = parsedCount;
            else
                TimeCount = 1;

            Setting? timePeriodSetting = settings.FirstOrDefault(x => x.Key == "TimePeriod");
            TimePeriod = timePeriodSetting?.Value ?? "Month";

            Setting? groupNameSetting = settings.FirstOrDefault(x => x.Key == "GroupName");
            GroupName = groupNameSetting?.Value ?? "Movie Club";

            // Load people and questions
            People = await PersonService.GetAllOrderedAsync(RespectOrder);

            // Auto-populate Order field for any people missing it (migration for existing database records)
            await EnsurePersonOrdersAsync();

            DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();

            // Load current themes
            SelectedGroupTheme = await themeService.GetGroupThemeAsync();
            await themeService.InitializeAsync();
            IsDarkMode = themeService.IsDarkMode;
            
            // Load available themes from ApplicationSettings
            ApplicationSettings applicationSettings = await SettingService.GetApplicationSettingsAsync();
            AvailableThemes = applicationSettings.AvailableThemes;
        }


        private async Task AddPerson()
        {
            if (string.IsNullOrWhiteSpace(NewPerson)) return;

            int newPersonOrder = People.Count + 1;
            _ = await PersonService.CreateAsync(new Person { Name = NewPerson, Order = newPersonOrder });
            NewPerson = "";
            People = await PersonService.GetAllOrderedAsync(RespectOrder);
        }

        private void Edit(Person person)
        {
            person.IsEditing = true;
        }

        private void Cancel(Person person)
        {
            person.IsEditing = false;
        }

        private async Task Delete(Person person)
        {
            _ = await PersonService.DeleteAsync(person.Id);
            People = await PersonService.GetAllOrderedAsync(RespectOrder);

            // Reorder remaining people
            for (int i = 0; i < People.Count; i++)
            {
                if (People[i].Order != i + 1)
                {
                    People[i].Order = i + 1;
                    _ = await PersonService.UpdateAsync(People[i]);
                }
            }
        }

        private async Task Save(Person person)
        {
            if (!demoProtection.TryValidateNotDemo("Save person", out string errorMessage))
            {
                // Show error message similar to API key errors
                StateHasChanged();
                return;
            }
            _ = await PersonService.UpsertAsync(person);
            person.IsEditing = false;
        }

        private async Task OnThemeChanged()
        {
            // Apply theme immediately when dropdown changes
            await themeService.SetGroupThemeAsync(SelectedGroupTheme);
            StateHasChanged();
        }

        private async Task SaveGeneralSettings()
        {
            // Always save dark mode to localStorage (local device preference)
            await themeService.SetDarkMode(IsDarkMode);

            // Save group theme to database only if not demo mode
            if (!demoProtection.IsDemoInstance)
            {
                await themeService.SetGroupThemeAsync(SelectedGroupTheme);
            }

            if (!demoProtection.TryValidateNotDemo("Save general settings", out string errorMessage))
            {
                // In demo mode, still allow theme toggle but show message for other settings
                StateHasChanged();
                return;
            }

            // Save Group Name as a setting (not instance name) - only in non-demo mode
            await SaveOrCreateSetting("GroupName", GroupName);

            // Save settings
            await SaveOrCreateSetting("RespectOrder", RespectOrder.ToString());
            await SaveOrCreateSetting("StartDate", StartDate.ToString());
            await SaveOrCreateSetting("TimeCount", TimeCount.ToString());
            await SaveOrCreateSetting("TimePeriod", TimePeriod);

            // Refresh people list if respect order changed
            People = await PersonService.GetAllOrderedAsync(RespectOrder);
            StateHasChanged();
        }

        private async Task SaveOrCreateSetting(string key, string value)
        {
            Setting? setting = settings.FirstOrDefault(x => x.Key == key);
            if (setting == null)
            {
                setting = new Setting
                {
                    Key = key,
                    Value = value,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                settings.Add(setting);
            }
            else
            {
                setting.Value = value;
                setting.UpdatedAt = DateTime.UtcNow;
            }
            await SettingService.AddOrUpdateSettingAsync(setting);
        }

        private async Task MoveUp(Person person)
        {
            if (!demoProtection.TryValidateNotDemo("Reorder Person", out string errorMessage))
            {
                apiMessage = errorMessage;
                apiMessageIsError = true;
                StateHasChanged();
                return;
            }

            if (person.Order <= 1) return;

            try
            {
                Person? personAbove = People.FirstOrDefault(p => p.Order == person.Order - 1);
                if (personAbove != null)
                {
                    personAbove.Order = person.Order;
                    person.Order = person.Order - 1;

                    _ = await PersonService.UpsertAsync(person);
                    _ = await PersonService.UpsertAsync(personAbove);
                    People = await PersonService.GetAllOrderedAsync(RespectOrder);
                }
            }
            catch (Exception ex)
            {
                apiMessage = $"Error reordering person: {ex.Message}";
                apiMessageIsError = true;
                StateHasChanged();
            }
        }

        private async Task MoveDown(Person person)
        {
            if (!demoProtection.TryValidateNotDemo("Reorder Person", out string errorMessage))
            {
                apiMessage = errorMessage;
                apiMessageIsError = true;
                StateHasChanged();
                return;
            }

            if (person.Order >= People.Count) return;

            try
            {
                Person? personBelow = People.FirstOrDefault(p => p.Order == person.Order + 1);
                if (personBelow != null)
                {
                    personBelow.Order = person.Order;
                    person.Order = person.Order + 1;

                    _ = await PersonService.UpsertAsync(person);
                    _ = await PersonService.UpsertAsync(personBelow);
                    People = await PersonService.GetAllOrderedAsync(RespectOrder);
                }
            }
            catch (Exception ex)
            {
                apiMessage = $"Error reordering person: {ex.Message}";
                apiMessageIsError = true;
                StateHasChanged();
            }
        }

        // Person Order Migration - Auto-populate Order field for existing database records
        private async Task EnsurePersonOrdersAsync()
        {
            bool needsUpdate = false;

            // Check if any people are missing Order values (Order = 0 indicates missing/default)
            List<Person> peopleNeedingOrders = People.Where(p => p.Order == 0).ToList();

            if (peopleNeedingOrders.Any())
            {
                // Find the highest existing Order value to continue sequence
                int maxExistingOrder = People.Where(p => p.Order > 0).DefaultIfEmpty(new Person { Order = 0 }).Max(p => p.Order);

                // Assign sequential order values starting from maxExistingOrder + 1
                for (int i = 0; i < peopleNeedingOrders.Count; i++)
                {
                    peopleNeedingOrders[i].Order = maxExistingOrder + i + 1;
                    needsUpdate = true;
                }

                // Save each person back to database to persist Order values
                foreach (Person person in peopleNeedingOrders)
                {
                    try
                    {
                        await PersonService.UpsertAsync(person);
                    }
                    catch (Exception ex)
                    {
                        // Log error but continue with other people
                        Console.WriteLine($"Error updating Order for person {person.Name}: {ex.Message}");
                    }
                }

                // If we updated any people, reload the list to show updated Order values
                if (needsUpdate)
                {
                    People = await PersonService.GetAllOrderedAsync(RespectOrder);
                }
            }
        }

        // Discussion Questions Management
        private async Task AddQuestion()
        {
            if (!demoProtection.TryValidateNotDemo("Add discussion question", out string errorMessage))
            {
                apiMessage = errorMessage;
                apiMessageIsError = true;
                StateHasChanged();
                return;
            }
            
            if (string.IsNullOrWhiteSpace(NewQuestionText)) return;

            int newQuestionOrder = DiscussionQuestions.Count + 1;
            _ = await discussionQuestionsService.CreateAsync(new DiscussionQuestion() { Question = NewQuestionText, Order = newQuestionOrder, IsActive = NewQuestionIsActive });
            NewQuestionText = "";
            NewQuestionIsActive = true;
            DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();
        }

        private void EditQuestion(DiscussionQuestion question)
        {
            question.IsEditing = true;
        }

        private async Task CancelQuestionEdit(DiscussionQuestion question)
        {
            question.IsEditing = false;
            DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();
        }

        private async Task SaveQuestion(DiscussionQuestion question)
        {
            if (!demoProtection.TryValidateNotDemo("Save discussion question", out string errorMessage))
            {
                apiMessage = errorMessage;
                apiMessageIsError = true;
                StateHasChanged();
                return;
            }
            
            _ = await discussionQuestionsService.UpdateAsync(question);
            question.IsEditing = false;
        }

        private async Task DeleteQuestion(DiscussionQuestion question)
        {
            if (!demoProtection.TryValidateNotDemo("Delete discussion question", out string errorMessage))
            {
                apiMessage = errorMessage;
                apiMessageIsError = true;
                StateHasChanged();
                return;
            }
            
            _ = await discussionQuestionsService.DeleteAsync(question.Id);
            DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();

            // Reorder remaining questions
            for (int i = 0; i < DiscussionQuestions.Count; i++)
            {
                if (DiscussionQuestions[i].Order != i + 1)
                {
                    DiscussionQuestions[i].Order = i + 1;
                    _ = await discussionQuestionsService.UpdateAsync(DiscussionQuestions[i]);
                }
            }
        }

        private async Task MoveQuestionUp(DiscussionQuestion question)
        {
            if (!demoProtection.TryValidateNotDemo("Reorder discussion questions", out string errorMessage))
            {
                apiMessage = errorMessage;
                apiMessageIsError = true;
                StateHasChanged();
                return;
            }
            
            if (question.Order <= 1) return;

            DiscussionQuestion? questionAbove = DiscussionQuestions.FirstOrDefault(q => q.Order == question.Order - 1);
            if (questionAbove != null)
            {
                questionAbove.Order = question.Order;
                question.Order = question.Order - 1;

                _ = await discussionQuestionsService.UpdateAsync(question);
                _ = await discussionQuestionsService.UpdateAsync(questionAbove);
                DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();
            }
        }

        private async Task MoveQuestionDown(DiscussionQuestion question)
        {
            if (!demoProtection.TryValidateNotDemo("Reorder discussion questions", out string errorMessage))
            {
                apiMessage = errorMessage;
                apiMessageIsError = true;
                StateHasChanged();
                return;
            }
            
            if (question.Order >= DiscussionQuestions.Count) return;

            DiscussionQuestion? questionBelow = DiscussionQuestions.FirstOrDefault(q => q.Order == question.Order + 1);
            if (questionBelow != null)
            {
                questionBelow.Order = question.Order;
                question.Order = question.Order + 1;

                _ = await discussionQuestionsService.UpdateAsync(question);
                _ = await discussionQuestionsService.UpdateAsync(questionBelow);
                DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();
            }
        }

        // Advanced Settings Methods
        private void ToggleAdvanced()
        {
            showAdvanced = !showAdvanced;
            if (showAdvanced)
            {
                LoadCurrentApiKeys();
            }
        }

        private void ToggleGeneral()
        {
            showGeneral = !showGeneral;
        }

        private void TogglePeople()
        {
            showPeople = !showPeople;
        }

        private void ToggleAwards()
        {
            showAwards = !showAwards;
        }

        private void ToggleQuestions()
        {
            showQuestions = !showQuestions;
        }

        private void LoadCurrentApiKeys()
        {
            // Don't pre-populate keys for security - just check if they exist
            openAIApiKey = "";
            claudeApiKey = "";
            tmdbApiKey = "";
            gladiaApiKey = "";
        }

        private async Task SaveApiKey(string provider, string apiKey)
        {
            try
            {
                if (!demoProtection.TryValidateNotDemo($"Save {provider} API key", out string errorMessage))
                {
                    apiMessage = errorMessage;
                    apiMessageIsError = true;
                    StateHasChanged();
                    return;
                }
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    apiMessage = $"{provider} API key cannot be empty";
                    apiMessageIsError = true;
                    return;
                }

                string secretKey = provider switch
                {
                    "OpenAI" => "OpenAI:ApiKey",
                    "Claude" => "Claude:ApiKey",
                    "TMDB" => "TMDB:ApiKey",
                    "Gladia" => "Gladia:ApiKey",
                    _ => throw new ArgumentException($"Unknown provider: {provider}")
                };

                // Save to encrypted storage
                secretsManager.SetSecret(secretKey, apiKey);

                apiMessage = $"{provider} API key saved successfully!";
                apiMessageIsError = false;

                // Clear the input field
                switch (provider)
                {
                    case "OpenAI": openAIApiKey = ""; break;
                    case "Claude": claudeApiKey = ""; break;
                    case "TMDB": tmdbApiKey = ""; break;
                    case "Gladia": gladiaApiKey = ""; break;
                }

                StateHasChanged();

                // Clear message after 3 seconds
                await Task.Delay(3000);
                apiMessage = "";
                StateHasChanged();
            }
            catch (Exception ex)
            {
                apiMessage = $"Error saving {provider} API key: {ex.Message}";
                apiMessageIsError = true;
            }
        }

        // Helper methods for API key saving
        private async void SaveOpenAIApiKey() => await SaveApiKey("OpenAI", openAIApiKey);
        private async void SaveClaudeApiKey() => await SaveApiKey("Claude", claudeApiKey);
        private async void SaveTMDBApiKey() => await SaveApiKey("TMDB", tmdbApiKey);
        private async void SaveGladiaApiKey() => await SaveApiKey("Gladia", gladiaApiKey);
    }
}
