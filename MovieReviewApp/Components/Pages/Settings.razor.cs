// Settings.razor.cs
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc.Rendering;
using MovieReviewApp.Models;
using MovieReviewApp.Services;

namespace MovieReviewApp.Components.Pages
{
    public partial class Settings : ComponentBase
    {
        [Inject]
        private MovieReviewService movieReviewService { get; set; } = default!;

        [Inject]
        private InstanceManager instanceManager { get; set; } = default!;

        [Inject]
        private NavigationManager navigationManager { get; set; } = default!;

        [Inject]
        private DiscussionQuestionsService discussionQuestionsService { get; set; } = default!;

        [Inject]
        private ThemeService themeService { get; set; } = default!;

        [Inject]
        private SecretsManager secretsManager { get; set; } = default!;

        [Inject]
        private OpenAIService openAIService { get; set; } = default!;

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
        public string SelectedTheme { get; set; } = "dark";

        private List<DiscussionQuestion> DiscussionQuestions { get; set; } = new();
        public string NewQuestionText { get; set; } = "";
        public bool NewQuestionIsActive { get; set; } = true;

        // Advanced Settings
        private bool showAdvanced = false;
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
            // Load settings
            settings = await movieReviewService.GetAllSettingsAsync();


            // Parse settings with defaults
            var respectOrderSetting = settings.FirstOrDefault(x => x.Key == "RespectOrder");
            RespectOrder = respectOrderSetting != null && bool.TryParse(respectOrderSetting.Value, out var respectOrderValue) ? respectOrderValue : false;

            var startDateSetting = settings.FirstOrDefault(x => x.Key == "StartDate");
            if (startDateSetting != null && DateTime.TryParse(startDateSetting.Value, out var parsedDate))
                StartDate = parsedDate;

            var timeCountSetting = settings.FirstOrDefault(x => x.Key == "TimeCount");
            if (timeCountSetting != null && int.TryParse(timeCountSetting.Value, out var parsedCount))
                TimeCount = parsedCount;

            var timePeriodSetting = settings.FirstOrDefault(x => x.Key == "TimePeriod");
            if (timePeriodSetting != null)
                TimePeriod = timePeriodSetting.Value;

            // Load group name
            var instanceConfig = instanceManager.GetInstanceConfig();
            GroupName = instanceConfig?.DisplayName ?? "";

            // Load people and questions
            People = await movieReviewService.GetAllPeopleAsync(RespectOrder);
            DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();

            // Load current theme
            SelectedTheme = await themeService.GetThemeAsync();
        }


        private async Task AddPerson()
        {
            if (string.IsNullOrWhiteSpace(NewPerson)) return;

            var newPersonOrder = People.Count + 1;
            await movieReviewService.AddPersonAsync(new Person { Name = NewPerson, Order = newPersonOrder });
            NewPerson = "";
            People = await movieReviewService.GetAllPeopleAsync(RespectOrder);
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
            await movieReviewService.DeletePersonAsync(person);
            People = await movieReviewService.GetAllPeopleAsync(RespectOrder);

            // Reorder remaining people
            for (int i = 0; i < People.Count; i++)
            {
                if (People[i].Order != i + 1)
                {
                    People[i].Order = i + 1;
                    await movieReviewService.AddOrUpdatePersonAsync(People[i]);
                }
            }
        }

        private async Task Save(Person person)
        {
            await movieReviewService.AddOrUpdatePersonAsync(person);
            person.IsEditing = false;
        }

        private async Task SaveGeneralSettings()
        {
            // Save Group Name
            var instanceConfig = instanceManager.GetInstanceConfig();
            instanceConfig.DisplayName = GroupName;
            instanceManager.SaveInstanceConfig(instanceConfig);

            // Save settings
            await SaveOrCreateSetting("RespectOrder", RespectOrder.ToString());
            await SaveOrCreateSetting("StartDate", StartDate.ToString());
            await SaveOrCreateSetting("TimeCount", TimeCount.ToString());
            await SaveOrCreateSetting("TimePeriod", TimePeriod);

            // Save theme
            await themeService.SetThemeAsync(SelectedTheme);

            // Refresh people list if respect order changed
            People = await movieReviewService.GetAllPeopleAsync(RespectOrder);
            StateHasChanged();
        }

        private async Task SaveOrCreateSetting(string key, string value)
        {
            var setting = settings.FirstOrDefault(x => x.Key == key);
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
            await movieReviewService.AddOrUpdateSettingAsync(setting);
        }

        private async Task MoveUp(Person person)
        {
            if (person.Order <= 1) return;

            var personAbove = People.FirstOrDefault(p => p.Order == person.Order - 1);
            if (personAbove != null)
            {
                personAbove.Order = person.Order;
                person.Order = person.Order - 1;

                await movieReviewService.AddOrUpdatePersonAsync(person);
                await movieReviewService.AddOrUpdatePersonAsync(personAbove);
                People = await movieReviewService.GetAllPeopleAsync(RespectOrder);
            }
        }

        private async Task MoveDown(Person person)
        {
            if (person.Order >= People.Count) return;

            var personBelow = People.FirstOrDefault(p => p.Order == person.Order + 1);
            if (personBelow != null)
            {
                personBelow.Order = person.Order;
                person.Order = person.Order + 1;

                await movieReviewService.AddOrUpdatePersonAsync(person);
                await movieReviewService.AddOrUpdatePersonAsync(personBelow);
                People = await movieReviewService.GetAllPeopleAsync(RespectOrder);
            }
        }

        // Discussion Questions Management
        private async Task AddQuestion()
        {
            if (string.IsNullOrWhiteSpace(NewQuestionText)) return;

            var newQuestionOrder = DiscussionQuestions.Count + 1;
            await discussionQuestionsService.CreateQuestionAsync(NewQuestionText, newQuestionOrder, NewQuestionIsActive);
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
            await discussionQuestionsService.UpdateQuestionAsync(question);
            question.IsEditing = false;
        }

        private async Task DeleteQuestion(DiscussionQuestion question)
        {
            await discussionQuestionsService.DeleteQuestionAsync(question.Id);
            DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();

            // Reorder remaining questions
            for (int i = 0; i < DiscussionQuestions.Count; i++)
            {
                if (DiscussionQuestions[i].Order != i + 1)
                {
                    DiscussionQuestions[i].Order = i + 1;
                    await discussionQuestionsService.UpdateQuestionAsync(DiscussionQuestions[i]);
                }
            }
        }

        private async Task MoveQuestionUp(DiscussionQuestion question)
        {
            if (question.Order <= 1) return;

            var questionAbove = DiscussionQuestions.FirstOrDefault(q => q.Order == question.Order - 1);
            if (questionAbove != null)
            {
                questionAbove.Order = question.Order;
                question.Order = question.Order - 1;

                await discussionQuestionsService.UpdateQuestionAsync(question);
                await discussionQuestionsService.UpdateQuestionAsync(questionAbove);
                DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();
            }
        }

        private async Task MoveQuestionDown(DiscussionQuestion question)
        {
            if (question.Order >= DiscussionQuestions.Count) return;

            var questionBelow = DiscussionQuestions.FirstOrDefault(q => q.Order == question.Order + 1);
            if (questionBelow != null)
            {
                questionBelow.Order = question.Order;
                question.Order = question.Order + 1;

                await discussionQuestionsService.UpdateQuestionAsync(question);
                await discussionQuestionsService.UpdateQuestionAsync(questionBelow);
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
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    apiMessage = $"{provider} API key cannot be empty";
                    apiMessageIsError = true;
                    return;
                }

                var secretKey = provider switch
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