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
        private List<Person>? People { get; set; } = new();
        private DateTime? StartDate { get; set; }
        public int? TimeCount;
        public string? TimePeriod;
        public string NewPerson = "New Person";
        public bool RespectOrder = false;
        public string GroupName = "";
        public List<Setting> settings = new List<Setting>();

        private List<DiscussionQuestion>? DiscussionQuestions { get; set; } = new();
        public string NewQuestionText = "";
        public bool NewQuestionIsActive = true;
        public readonly List<SelectListItem> TimePeriods = new List<SelectListItem>
        {
            new SelectListItem { Value = "Month", Text = "Month" },
            new SelectListItem { Value = "Week", Text = "Week" },
            new SelectListItem { Value = "Day", Text = "Day" }
        };


        protected override async Task OnInitializedAsync()
        {
            settings = await movieReviewService.GetSettingsAsync();
            RespectOrder = false; // default value
            var setting = settings.FirstOrDefault(x => x.Key == "RespectOrder");
            if (setting != null && !string.IsNullOrEmpty(setting.Value))
                bool.TryParse(setting.Value, out RespectOrder);

            // Safe parsing with defaults
            var startDateSetting = settings.FirstOrDefault(x => x.Key == "StartDate");
            if (startDateSetting != null && DateTime.TryParse(startDateSetting.Value, out var parsedStartDate))
                StartDate = parsedStartDate;
            else
                StartDate = DateTime.Now;

            var timeCountSetting = settings.FirstOrDefault(x => x.Key == "TimeCount");
            if (timeCountSetting != null && int.TryParse(timeCountSetting.Value, out var parsedTimeCount))
                TimeCount = parsedTimeCount;
            else
                TimeCount = 1;

            var timePeriodSetting = settings.FirstOrDefault(x => x.Key == "TimePeriod");
            TimePeriod = timePeriodSetting?.Value ?? "Month";

            // Load group name from instance config
            var instanceConfig = instanceManager.GetInstanceConfig();
            GroupName = instanceConfig.DisplayName;

            People = await movieReviewService.GetAllPeopleAsync(RespectOrder);
            await EnsureValidOrder();

            // Load discussion questions
            DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();
        }

        private async Task EnsureValidOrder()
        {
            if (People == null) return;

            var orderedPeople = People
                .OrderBy(p => p.Order == 0)
                .ThenBy(p => p.Order)
                .ThenBy(p => p.Name)
                .ToList();

            for (int i = 0; i < orderedPeople.Count; i++)
            {
                if (orderedPeople[i].Order != i + 1)
                {
                    orderedPeople[i].Order = i + 1;
                    await movieReviewService.AddOrUpdatePersonAsync(orderedPeople[i]);
                }
            }

            People = await movieReviewService.GetAllPeopleAsync(RespectOrder);
        }

        private async Task AddPerson()
        {
            var newPersonOrder = (People?.Count ?? 0) + 1;
            await movieReviewService.AddPersonAsync(new Person { Name = NewPerson, Order = newPersonOrder });
            NewPerson = "New Person";
            People = await movieReviewService.GetAllPeopleAsync(RespectOrder);
        }

        private void Edit(Person person)
        {
            if (person != null)
                person.IsEditing = true;
        }

        private void Cancel(Person person)
        {
            if (person != null)
                person.IsEditing = false;
        }

        private async Task Delete(Person person)
        {
            if (person != null)
            {
                await movieReviewService.DeletePersonAsync(person);
                People = await movieReviewService.GetAllPeopleAsync(RespectOrder);
                await EnsureValidOrder(); // Reorder after deletion
            }
        }

        private async Task Save(Person person)
        {
            if (person != null)
            {
                await movieReviewService.AddOrUpdatePersonAsync(person);
                this.Cancel(person);
            }
        }

        private async Task SaveDate()
        {
            var dateSetting = settings.First(x => x.Key == "StartDate");
            dateSetting.Value = StartDate.ToString();
            await movieReviewService.AddOrUpdateSettingAsync(dateSetting);
        }

        private async Task SaveOccurance(Microsoft.AspNetCore.Components.Web.MouseEventArgs e)
        {
            var timeCountSetting = settings.First(x => x.Key == "TimeCount");
            timeCountSetting.Value = TimeCount.ToString();
            await movieReviewService.AddOrUpdateSettingAsync(timeCountSetting);

            var timePeriodSetting = settings.First(x => x.Key == "TimePeriod");
            timePeriodSetting.Value = TimePeriod;
            await movieReviewService.AddOrUpdateSettingAsync(timePeriodSetting);
        }

        private async Task SaveRespectOrder()
        {
            var orderSetting = settings.FirstOrDefault(x => x.Key == "RespectOrder");
            if (orderSetting == null)
            {
                orderSetting = new Setting { Key = "RespectOrder", Value = RespectOrder.ToString() };
                settings.Add(orderSetting);
            }
            else
            {
                orderSetting.Value = RespectOrder.ToString();
            }
            await movieReviewService.AddOrUpdateSettingAsync(orderSetting);
            People = await movieReviewService.GetAllPeopleAsync(RespectOrder);
        }

        private async Task SaveGeneralSettings()
        {
            // Save Group Name to instance config
            var instanceConfig = instanceManager.GetInstanceConfig();
            instanceConfig.DisplayName = GroupName;
            instanceManager.SaveInstanceConfig(instanceConfig);

            // Save Respect Order
            var orderSetting = settings.FirstOrDefault(x => x.Key == "RespectOrder");
            if (orderSetting == null)
            {
                orderSetting = new Setting { Key = "RespectOrder", Value = RespectOrder.ToString() };
                settings.Add(orderSetting);
            }
            else
            {
                orderSetting.Value = RespectOrder.ToString();
            }
            await movieReviewService.AddOrUpdateSettingAsync(orderSetting);

            // Save Start Date
            var dateSetting = settings.First(x => x.Key == "StartDate");
            dateSetting.Value = StartDate.ToString();
            await movieReviewService.AddOrUpdateSettingAsync(dateSetting);

            // Save Time Count
            var timeCountSetting = settings.First(x => x.Key == "TimeCount");
            timeCountSetting.Value = TimeCount.ToString();
            await movieReviewService.AddOrUpdateSettingAsync(timeCountSetting);

            // Save Time Period
            var timePeriodSetting = settings.First(x => x.Key == "TimePeriod");
            timePeriodSetting.Value = TimePeriod;
            await movieReviewService.AddOrUpdateSettingAsync(timePeriodSetting);

            // Refresh people list with updated respect order setting
            People = await movieReviewService.GetAllPeopleAsync(RespectOrder);

            // Update UI state instead of forcing a full page reload
            StateHasChanged();
        }

        private async Task MoveUp(Person person)
        {
            if (person?.Order > 1)
            {
                var personAbove = People?.FirstOrDefault(p => p.Order == person.Order - 1);
                if (personAbove != null)
                {
                    var tempOrder = personAbove.Order;
                    personAbove.Order = person.Order;
                    person.Order = tempOrder;

                    await movieReviewService.AddOrUpdatePersonAsync(person);
                    await movieReviewService.AddOrUpdatePersonAsync(personAbove);

                    People = await movieReviewService.GetAllPeopleAsync(RespectOrder);
                }
            }
        }

        private async Task MoveDown(Person person)
        {
            if (person?.Order < People?.Count)
            {
                var personBelow = People?.FirstOrDefault(p => p.Order == person.Order + 1);
                if (personBelow != null)
                {
                    var tempOrder = personBelow.Order;
                    personBelow.Order = person.Order;
                    person.Order = tempOrder;

                    await movieReviewService.AddOrUpdatePersonAsync(person);
                    await movieReviewService.AddOrUpdatePersonAsync(personBelow);

                    People = await movieReviewService.GetAllPeopleAsync(RespectOrder);
                }
            }
        }

        // Discussion Questions Management
        private async Task AddQuestion()
        {
            if (!string.IsNullOrWhiteSpace(NewQuestionText))
            {
                var newQuestionOrder = (DiscussionQuestions?.Count ?? 0) + 1;
                await discussionQuestionsService.CreateQuestionAsync(NewQuestionText, newQuestionOrder, NewQuestionIsActive);
                NewQuestionText = "";
                NewQuestionIsActive = true;
                DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();
            }
        }

        private void EditQuestion(DiscussionQuestion question)
        {
            if (question != null)
                question.IsEditing = true;
        }

        private async Task CancelQuestionEdit(DiscussionQuestion question)
        {
            if (question != null)
            {
                question.IsEditing = false;
                // Reload to revert changes
                DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();
            }
        }

        private async Task SaveQuestion(DiscussionQuestion question)
        {
            if (question != null)
            {
                await discussionQuestionsService.UpdateQuestionAsync(question);
                question.IsEditing = false;
                DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();
            }
        }

        private async Task DeleteQuestion(DiscussionQuestion question)
        {
            if (question != null)
            {
                await discussionQuestionsService.DeleteQuestionAsync(question.Id);
                DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();
                await EnsureValidQuestionOrder();
            }
        }

        private async Task MoveQuestionUp(DiscussionQuestion question)
        {
            if (question?.Order > 1)
            {
                var questionAbove = DiscussionQuestions?.FirstOrDefault(q => q.Order == question.Order - 1);
                if (questionAbove != null)
                {
                    var tempOrder = questionAbove.Order;
                    questionAbove.Order = question.Order;
                    question.Order = tempOrder;

                    await discussionQuestionsService.UpdateQuestionAsync(question);
                    await discussionQuestionsService.UpdateQuestionAsync(questionAbove);

                    DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();
                }
            }
        }

        private async Task MoveQuestionDown(DiscussionQuestion question)
        {
            if (question?.Order < DiscussionQuestions?.Count)
            {
                var questionBelow = DiscussionQuestions?.FirstOrDefault(q => q.Order == question.Order + 1);
                if (questionBelow != null)
                {
                    var tempOrder = questionBelow.Order;
                    questionBelow.Order = question.Order;
                    question.Order = tempOrder;

                    await discussionQuestionsService.UpdateQuestionAsync(question);
                    await discussionQuestionsService.UpdateQuestionAsync(questionBelow);

                    DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();
                }
            }
        }

        private async Task EnsureValidQuestionOrder()
        {
            if (DiscussionQuestions == null) return;

            var orderedQuestions = DiscussionQuestions
                .OrderBy(q => q.Order == 0)
                .ThenBy(q => q.Order)
                .ThenBy(q => q.Question)
                .ToList();

            for (int i = 0; i < orderedQuestions.Count; i++)
            {
                if (orderedQuestions[i].Order != i + 1)
                {
                    orderedQuestions[i].Order = i + 1;
                    await discussionQuestionsService.UpdateQuestionAsync(orderedQuestions[i]);
                }
            }

            DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();
        }
    }
}