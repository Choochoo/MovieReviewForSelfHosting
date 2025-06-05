// Settings.razor.cs
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc.Rendering;
using MovieReviewApp.Models;
using MovieReviewApp.Services;

namespace MovieReviewApp.Components.Pages
{
    public partial class Settings
    {
        [Inject]
        private MovieReviewService movieReviewService { get; set; } = default!;
        
        [Inject]
        private InstanceManager instanceManager { get; set; } = default!;
        
        [Inject]
        private NavigationManager navigationManager { get; set; } = default!;
        
        [Inject]
        private DiscussionQuestionsService discussionQuestionsService { get; set; } = default!;
        public List<Person>? People { get; set; }
        public DateTime? StartDate { get; set; }
        public int? TimeCount;
        public string? TimePeriod;
        public string NewPerson = "New Person";
        public bool RespectOrder = false;
        public string GroupName = "";
        public List<Setting> settings = new List<Setting>();
        
        public List<DiscussionQuestion>? DiscussionQuestions { get; set; }
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
            settings = movieReviewService.GetSettings();
            RespectOrder = false; // default value
            var setting = settings.FirstOrDefault(x => x.Key == "RespectOrder");
            if (setting != null && !string.IsNullOrEmpty(setting.Value))
                bool.TryParse(setting.Value, out RespectOrder);
            StartDate = DateTime.Parse(settings.First(x => x.Key == "StartDate").Value);
            TimeCount = int.Parse(settings.First(x => x.Key == "TimeCount").Value);
            TimePeriod = settings.First(x => x.Key == "TimePeriod").Value;
            
            // Load group name from instance config
            var instanceConfig = instanceManager.GetInstanceConfig();
            GroupName = instanceConfig.DisplayName;
            
            People = movieReviewService.GetAllPeople(RespectOrder);
            EnsureValidOrder();
            
            // Load discussion questions
            DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();
        }

        private void EnsureValidOrder()
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
                    movieReviewService.AddOrUpdatePerson(orderedPeople[i]);
                }
            }

            People = movieReviewService.GetAllPeople(RespectOrder);
        }

        private void AddPerson()
        {
            var newPersonOrder = (People?.Count ?? 0) + 1;
            movieReviewService.AddPerson(new Person { Name = NewPerson, Order = newPersonOrder });
            NewPerson = "New Person";
            People = movieReviewService.GetAllPeople(RespectOrder);
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

        private void Delete(Person person)
        {
            if (person != null)
            {
                movieReviewService.DeletePerson(person);
                People = movieReviewService.GetAllPeople(RespectOrder);
                EnsureValidOrder(); // Reorder after deletion
            }
        }

        private void Save(Person person)
        {
            if (person != null)
            {
                movieReviewService.AddOrUpdatePerson(person);
                this.Cancel(person);
            }
        }

        private void SaveDate()
        {
            var dateSetting = settings.First(x => x.Key == "StartDate");
            dateSetting.Value = StartDate.ToString();
            movieReviewService.AddOrUpdateSetting(dateSetting);
        }

        private void SaveOccurance(Microsoft.AspNetCore.Components.Web.MouseEventArgs e)
        {
            var timeCountSetting = settings.First(x => x.Key == "TimeCount");
            timeCountSetting.Value = TimeCount.ToString();
            movieReviewService.AddOrUpdateSetting(timeCountSetting);

            var timePeriodSetting = settings.First(x => x.Key == "TimePeriod");
            timePeriodSetting.Value = TimePeriod;
            movieReviewService.AddOrUpdateSetting(timePeriodSetting);
        }

        private void SaveRespectOrder()
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
            movieReviewService.AddOrUpdateSetting(orderSetting);
            People = movieReviewService.GetAllPeople(RespectOrder);
        }

        private void SaveGeneralSettings()
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
            movieReviewService.AddOrUpdateSetting(orderSetting);

            // Save Start Date
            var dateSetting = settings.First(x => x.Key == "StartDate");
            dateSetting.Value = StartDate.ToString();
            movieReviewService.AddOrUpdateSetting(dateSetting);

            // Save Time Count
            var timeCountSetting = settings.First(x => x.Key == "TimeCount");
            timeCountSetting.Value = TimeCount.ToString();
            movieReviewService.AddOrUpdateSetting(timeCountSetting);

            // Save Time Period
            var timePeriodSetting = settings.First(x => x.Key == "TimePeriod");
            timePeriodSetting.Value = TimePeriod;
            movieReviewService.AddOrUpdateSetting(timePeriodSetting);

            // Refresh people list with updated respect order setting
            People = movieReviewService.GetAllPeople(RespectOrder);
            
            // Force navigation refresh to update the title in NavMenu
            navigationManager.NavigateTo(navigationManager.Uri, forceLoad: true);
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

                    movieReviewService.AddOrUpdatePerson(person);
                    movieReviewService.AddOrUpdatePerson(personAbove);

                    People = movieReviewService.GetAllPeople(RespectOrder);
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

                    movieReviewService.AddOrUpdatePerson(person);
                    movieReviewService.AddOrUpdatePerson(personBelow);

                    People = movieReviewService.GetAllPeople(RespectOrder);
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

        private void CancelQuestionEdit(DiscussionQuestion question)
        {
            if (question != null)
            {
                question.IsEditing = false;
                // Reload to revert changes
                Task.Run(async () =>
                {
                    DiscussionQuestions = await discussionQuestionsService.GetAllQuestionsAsync();
                    StateHasChanged();
                });
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