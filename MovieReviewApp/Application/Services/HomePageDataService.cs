using MongoDB.Driver;
using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Models;
using MovieReviewApp.Models.ViewModels;
using MovieReviewApp.Utilities;
using System.Collections.Concurrent;

namespace MovieReviewApp.Application.Services
{
    /// <summary>
    /// Service to fetch all home page data in a single optimized operation
    /// </summary>
    public class HomePageDataService
    {
        private readonly MongoDbService _db;
        private readonly ILogger<HomePageDataService> _logger;

        public HomePageDataService(MongoDbService databaseService, ILogger<HomePageDataService> logger)
        {
            _db = databaseService;
            _logger = logger;
        }

        /// <summary>
        /// Fetches all data needed for the home page in parallel
        /// </summary>
        public async Task<HomePageViewModel> GetHomePageDataAsync(DateTime? lastVisit = null)
        {
            HomePageViewModel viewModel = new HomePageViewModel();

            // Create all tasks for parallel execution
            List<Task> tasks = new List<Task>();

            // Basic data fetching tasks
            Task<List<DiscussionQuestion>> discussionQuestionsTask = GetActiveDiscussionQuestionsAsync();
            Task<List<Person>> peopleTask = _db.GetAllAsync<Person>();
            Task<List<Setting>> settingsTask = _db.GetAllAsync<Setting>();
            Task<List<MovieEvent>> eventsTask = _db.GetAllAsync<MovieEvent>();
            Task<long> eventCountTask = _db.CountAsync<MovieEvent>();
            Task<List<Phase>> phasesTask = _db.GetAllAsync<Phase>();
            Task<List<AwardEvent>> awardEventsTask = _db.GetAllAsync<AwardEvent>();
            Task<List<AwardQuestion>> awardQuestionsTask = GetActiveAwardQuestionsAsync();

            // Add all tasks to list
            tasks.Add(Task.Run(async () => viewModel.DiscussionQuestions = await discussionQuestionsTask));
            tasks.Add(Task.Run(async () => viewModel.AllPeople = await peopleTask));
            tasks.Add(Task.Run(async () => viewModel.Settings = await settingsTask));
            tasks.Add(Task.Run(async () => viewModel.ExistingEvents = await eventsTask));
            tasks.Add(Task.Run(async () => viewModel.ExistingEventCount = (int)await eventCountTask));
            tasks.Add(Task.Run(async () => viewModel.DbPhases = (await phasesTask).OrderBy(p => p.StartDate).ToList()));
            tasks.Add(Task.Run(async () => viewModel.AllAwardEvents = await awardEventsTask));
            tasks.Add(Task.Run(async () => viewModel.AllAwardQuestions = await awardQuestionsTask));

            // Site updates task (conditional)
            if (lastVisit.HasValue)
            {
                Task<List<SiteUpdate>> updatesTask = GetRecentSiteUpdatesAsync(lastVisit.Value);
                tasks.Add(Task.Run(async () => viewModel.RecentUpdates = await updatesTask));
            }

            // Wait for all basic data to load
            await Task.WhenAll(tasks);

            // Process settings
            ProcessSettings(viewModel);

            // Determine current award phase
            await DetermineAwardPhaseAsync(viewModel);

            // Get award settings
            viewModel.AwardSettings = await GetAwardSettingsAsync(viewModel.Settings);

            // Determine current and next events
            await DetermineCurrentAndNextEventsAsync(viewModel);

            // Load question results for all award events
            await LoadAllQuestionResultsAsync(viewModel);

            return viewModel;
        }

        private async Task<List<DiscussionQuestion>> GetActiveDiscussionQuestionsAsync()
        {
            FilterDefinition<DiscussionQuestion> filter = Builders<DiscussionQuestion>.Filter.Eq(q => q.IsActive, true);
            return await _db.FindAsync(filter);
        }

        private async Task<List<AwardQuestion>> GetActiveAwardQuestionsAsync()
        {
            FilterDefinition<AwardQuestion> filter = Builders<AwardQuestion>.Filter.Eq(q => q.IsActive, true);
            return await _db.FindAsync(filter);
        }

        private async Task<List<SiteUpdate>> GetRecentSiteUpdatesAsync(DateTime since)
        {
            FilterDefinition<SiteUpdate> filter = Builders<SiteUpdate>.Filter.Gte(u => u.UpdatedAt, since);
            SortDefinition<SiteUpdate> sort = Builders<SiteUpdate>.Sort.Descending(u => u.UpdatedAt);
            return await _db.FindAsync(filter, sort, limit: 10);
        }

        private void ProcessSettings(HomePageViewModel viewModel)
        {
            // Parse start date
            string? startDateSetting = viewModel.Settings.FirstOrDefault(x => x.Key == "StartDate")?.Value;
            if (!string.IsNullOrEmpty(startDateSetting) && DateTime.TryParse(startDateSetting, out DateTime date))
            {
                viewModel.StartDate = date;
            }

            // Parse respect order
            Setting? respectOrderSetting = viewModel.Settings.FirstOrDefault(x => x.Key == "RespectOrder");
            viewModel.RespectOrder = respectOrderSetting != null &&
                                   !string.IsNullOrEmpty(respectOrderSetting.Value) &&
                                   bool.TryParse(respectOrderSetting.Value, out bool respect) &&
                                   respect;

            // Extract names
            viewModel.AllNames = viewModel.AllPeople
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrEmpty(x))
                .ToArray();
        }

        private async Task DetermineAwardPhaseAsync(HomePageViewModel viewModel)
        {
            // Check if we're in an award phase
            DateTime now = DateProvider.Now;
            FilterDefinition<AwardEvent> filter = Builders<AwardEvent>.Filter.And(
                Builders<AwardEvent>.Filter.Lte(ae => ae.StartDate, now),
                Builders<AwardEvent>.Filter.Gte(ae => ae.EndDate, now)
            );

            List<AwardEvent> currentAwardEvents = await _db.FindAsync(filter, null, 1);
            AwardEvent? currentAwardEvent = currentAwardEvents.FirstOrDefault();
            viewModel.IsCurrentPhaseAwardPhase = currentAwardEvent != null;
        }

        private async Task<AwardSetting?> GetAwardSettingsAsync(List<Setting> settings)
        {
            Setting? awardSettingEntry = settings.FirstOrDefault(s => s.Key == "AwardSettings");

            if (awardSettingEntry != null && !string.IsNullOrEmpty(awardSettingEntry.Value))
            {
                return System.Text.Json.JsonSerializer.Deserialize<AwardSetting>(awardSettingEntry.Value);
            }

            // Return default if not found
            return new AwardSetting { PhasesBeforeAward = 3 };
        }

        private async Task DetermineCurrentAndNextEventsAsync(HomePageViewModel viewModel)
        {
            DateTime now = DateProvider.Now;
            
            // Current event
            FilterDefinition<MovieEvent> currentFilter = Builders<MovieEvent>.Filter.And(
                Builders<MovieEvent>.Filter.Lte(e => e.StartDate, now),
                Builders<MovieEvent>.Filter.Gte(e => e.EndDate, now)
            );
            List<MovieEvent> currentEvents = await _db.FindAsync(currentFilter, null, 1);
            viewModel.CurrentEvent = currentEvents.FirstOrDefault();

            if (viewModel.CurrentEvent == null)
            {
                // Get most recent past event
                SortDefinition<MovieEvent> sort = Builders<MovieEvent>.Sort.Descending(e => e.EndDate);
                FilterDefinition<MovieEvent> pastFilter = Builders<MovieEvent>.Filter.Lt(e => e.EndDate, now);
                List<MovieEvent> pastEvents = await _db.FindAsync(pastFilter, sort, limit: 1);
                viewModel.CurrentEvent = pastEvents.FirstOrDefault();
                viewModel.IsShowingPastEvent = viewModel.CurrentEvent != null;
            }

            // Next event
            FilterDefinition<MovieEvent> nextFilter = Builders<MovieEvent>.Filter.Gt(e => e.StartDate, now);
            SortDefinition<MovieEvent> nextSort = Builders<MovieEvent>.Sort.Ascending(e => e.StartDate);
            List<MovieEvent> nextEvents = await _db.FindAsync(nextFilter, nextSort, limit: 1);
            viewModel.NextEvent = nextEvents.FirstOrDefault();
        }

        private async Task LoadAllQuestionResultsAsync(HomePageViewModel viewModel)
        {
            if (!viewModel.AllAwardEvents.Any() || !viewModel.AllAwardQuestions.Any())
                return;

            // Get all votes and movie events in parallel
            Task<List<AwardVote>> votesTask = _db.GetAllAsync<AwardVote>();
            Task<List<MovieEvent>> eventsTask = _db.GetAllAsync<MovieEvent>();
            
            await Task.WhenAll(votesTask, eventsTask);
            
            List<AwardVote> allVotes = await votesTask;
            List<MovieEvent> allEvents = await eventsTask;

            // Process results for each award event and question combination
            foreach (AwardEvent awardEvent in viewModel.AllAwardEvents)
            {
                foreach (AwardQuestion question in viewModel.AllAwardQuestions.Where(q => awardEvent.Questions.Contains(q.Id)))
                {
                    List<QuestionResult> results = allVotes
                        .Where(v => v.AwardEventId == awardEvent.Id && v.QuestionId == question.Id)
                        .GroupBy(v => v.MovieEventId)
                        .Select(g =>
                        {
                            MovieEvent? movieEvent = allEvents.FirstOrDefault(e => e.Id == g.Key);
                            string movieTitle = movieEvent?.Movie ?? "Unknown Movie";
                            return new QuestionResult
                            {
                                MovieTitle = movieTitle,
                                TotalPoints = g.Sum(v => v.Points),
                                FirstPlaceVotes = g.Count(v => v.Points == 3),
                                SecondPlaceVotes = g.Count(v => v.Points == 2),
                                ThirdPlaceVotes = g.Count(v => v.Points == 1)
                            };
                        })
                        .OrderByDescending(r => r.TotalPoints)
                        .ThenByDescending(r => r.FirstPlaceVotes)
                        .ThenByDescending(r => r.SecondPlaceVotes)
                        .ToList();
                    
                    viewModel.QuestionResults[(awardEvent.Id, question.Id)] = results;
                }
            }
        }
    }
}