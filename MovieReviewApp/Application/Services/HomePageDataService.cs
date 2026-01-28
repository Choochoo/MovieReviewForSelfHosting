using MovieReviewApp.Extensions;
using MovieReviewApp.Infrastructure.Configuration;
using MovieReviewApp.Infrastructure.Services;
using MovieReviewApp.Models;
using MovieReviewApp.Models.ViewModels;
using MovieReviewApp.Utilities;
using System.Collections.Concurrent;

namespace MovieReviewApp.Application.Services
{
    /// <summary>
    /// Service to fetch all home page data in a single optimized operation
    /// Uses proper domain services to maintain separation of concerns
    /// </summary>
    public class HomePageDataService
    {
        private readonly ILogger<HomePageDataService> _logger;
        private readonly SettingService _settingService;
        private readonly PersonService _personService;
        private readonly MovieEventService _movieEventService;
        private readonly AwardEventService _awardEventService;
        private readonly AwardQuestionService _awardQuestionService;
        private readonly DiscussionQuestionService _discussionQuestionService;
        private readonly SiteUpdateService _siteUpdateService;
        private readonly AwardVoteService _awardVoteService;
        private readonly CategoryVotingService _categoryVotingService;

        public HomePageDataService(
            ILogger<HomePageDataService> logger,
            SettingService settingService,
            PersonService personService,
            MovieEventService movieEventService,
            AwardEventService awardEventService,
            AwardQuestionService awardQuestionService,
            DiscussionQuestionService discussionQuestionService,
            SiteUpdateService siteUpdateService,
            AwardVoteService awardVoteService,
            CategoryVotingService categoryVotingService)
        {
            _logger = logger;
            _settingService = settingService;
            _personService = personService;
            _movieEventService = movieEventService;
            _awardEventService = awardEventService;
            _awardQuestionService = awardQuestionService;
            _discussionQuestionService = discussionQuestionService;
            _siteUpdateService = siteUpdateService;
            _awardVoteService = awardVoteService;
            _categoryVotingService = categoryVotingService;
        }

        /// <summary>
        /// Fetches all data needed for the home page in parallel
        /// </summary>
        public async Task<HomePageViewModel> GetHomePageDataAsync(DateTime? lastVisit = null)
        {
            HomePageViewModel viewModel = new HomePageViewModel();

            // Calculate date range once - 1 month back, 2 months forward
            DateTime now = DateProvider.Now;
            DateTime rangeStart = now.AddMonths(-1).StartOfMonth();
            DateTime rangeEnd = now.AddMonths(2).EndOfMonth();

            // Create all tasks for parallel execution
            List<Task> tasks = new List<Task>();

            // Basic data fetching tasks using domain services
            Task<List<DiscussionQuestion>> discussionQuestionsTask = GetActiveDiscussionQuestionsAsync();
            Task<List<Person>> peopleTask = _personService.GetAllAsync();
            Task<List<Setting>> settingsTask = _settingService.GetAllAsync();
            Task<List<MovieEvent>> eventsTask = _movieEventService.GetByDateRangeAsync(rangeStart, rangeEnd);
            Task<long> eventCountTask = _movieEventService.GetCountAsync();
            Task<AwardEvent?> lastCompletedAwardTask = _awardEventService.GetLastCompletedAsync();
            Task<List<AwardEvent>> awardEventsTask = _awardEventService.GetAllAsync();
            Task<List<AwardQuestion>> awardQuestionsTask = GetActiveAwardQuestionsAsync();

            // Add all tasks to list
            tasks.Add(Task.Run(async () => viewModel.DiscussionQuestions = await discussionQuestionsTask));
            tasks.Add(Task.Run(async () => viewModel.AllPeople = await peopleTask));
            tasks.Add(Task.Run(async () => viewModel.Settings = await settingsTask));
            tasks.Add(Task.Run(async () => viewModel.ExistingEvents = await eventsTask));
            tasks.Add(Task.Run(async () => viewModel.ExistingEventCount = (int)await eventCountTask));
            tasks.Add(Task.Run(async () => viewModel.LastCompletedAward = await lastCompletedAwardTask));
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

            // Ensure current and next months exist in database
            await EnsureCurrentAndNextMonthExistAsync(viewModel);

            // Process settings
            ProcessSettings(viewModel);

            // Determine current award phase
            await DetermineAwardPhaseAsync(viewModel);

            // Determine if we're in a pre-awards voting month
            await DeterminePreAwardsVotingAsync(viewModel);

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
            // Use repository through the domain service to get active questions
            List<DiscussionQuestion> allQuestions = await _discussionQuestionService.GetAllAsync();
            return allQuestions.Where(q => q.IsActive).OrderBy(q => q.Order).ToList();
        }

        private async Task<List<AwardQuestion>> GetActiveAwardQuestionsAsync()
        {
            // Use repository through the domain service to get active award questions
            List<AwardQuestion> allQuestions = await _awardQuestionService.GetAllAsync();
            return allQuestions.Where(q => q.IsActive).ToList();
        }

        private async Task<List<SiteUpdate>> GetRecentSiteUpdatesAsync(DateTime since)
        {
            // Use repository through the domain service to get recent updates
            List<SiteUpdate> allUpdates = await _siteUpdateService.GetAllAsync();
            return allUpdates
                .Where(u => u.UpdatedAt >= since)
                .OrderByDescending(u => u.UpdatedAt)
                .Take(10)
                .ToList();
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

            // Extract names ordered by Order field
            viewModel.AllNames = viewModel.AllPeople
                .OrderBy(x => x.Order)  // CRITICAL: Order by Order field first!
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrEmpty(x))
                .ToArray();
        }

        private async Task DetermineAwardPhaseAsync(HomePageViewModel viewModel)
        {
            // Check if we're in an award phase using domain service
            DateTime now = DateProvider.Now;

            // Get all award events and filter using LINQ
            List<AwardEvent> allAwardEvents = await _awardEventService.GetAllAsync();
            AwardEvent? currentAwardEvent = allAwardEvents
                .FirstOrDefault(ae => ae.StartDate <= now && ae.EndDate >= now);

            viewModel.IsCurrentPhaseAwardPhase = currentAwardEvent != null;
        }

        private async Task DeterminePreAwardsVotingAsync(HomePageViewModel viewModel)
        {
            // Skip if we're already in an award phase
            if (viewModel.IsCurrentPhaseAwardPhase)
            {
                _logger.LogInformation("DeterminePreAwardsVotingAsync: Skipping - already in award phase");
                viewModel.IsPreAwardsVotingMonth = false;
                return;
            }

            _logger.LogInformation("DeterminePreAwardsVotingAsync: Checking if pre-awards month for {Date}", DateProvider.Now);

            // Check if we're in a pre-awards voting month
            try
            {
                viewModel.IsPreAwardsVotingMonth = await _categoryVotingService.IsPreAwardsMonthAsync();
                _logger.LogInformation("DeterminePreAwardsVotingAsync: IsPreAwardsMonthAsync returned {Result}", viewModel.IsPreAwardsVotingMonth);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeterminePreAwardsVotingAsync: Error checking pre-awards month");
                viewModel.IsPreAwardsVotingMonth = false;
                return;
            }

            if (viewModel.IsPreAwardsVotingMonth)
            {
                _logger.LogInformation("Currently in pre-awards voting month");

                // Get or create the category voting event (this will also generate categories if needed)
                viewModel.CurrentCategoryVotingEvent = await _categoryVotingService.GetOrCreateCurrentEventAsync();

                if (viewModel.CurrentCategoryVotingEvent != null)
                {
                    _logger.LogInformation(
                        "Category voting event loaded: {EventId} with {CategoryCount} categories",
                        viewModel.CurrentCategoryVotingEvent.Id,
                        viewModel.CurrentCategoryVotingEvent.GeneratedCategories.Count);
                }
            }
        }

        private async Task<AwardSetting?> GetAwardSettingsAsync(List<Setting> settings)
        {
            return await _settingService.GetAwardSettingsAsync();
        }

        private async Task DetermineCurrentAndNextEventsAsync(HomePageViewModel viewModel)
        {
            DateTime now = DateProvider.Now;

            // Use already-loaded events from view model (avoids redundant database query)
            List<MovieEvent> allEvents = viewModel.ExistingEvents;

            // Current event - find event that contains current date
            viewModel.CurrentEvent = allEvents
                .FirstOrDefault(e => e.StartDate <= now && e.EndDate >= now);

            if (viewModel.CurrentEvent == null)
            {
                // Get most recent past event
                viewModel.CurrentEvent = allEvents
                    .Where(e => e.EndDate < now)
                    .OrderByDescending(e => e.EndDate)
                    .FirstOrDefault();
                viewModel.IsShowingPastEvent = viewModel.CurrentEvent != null;
            }

            // Next event - find earliest future event
            viewModel.NextEvent = allEvents
                .Where(e => e.StartDate > now)
                .OrderBy(e => e.StartDate)
                .FirstOrDefault();
        }

        private async Task LoadAllQuestionResultsAsync(HomePageViewModel viewModel)
        {
            if (!viewModel.AllAwardQuestions.Any())
                return;

            // Build list of relevant award events (last completed + all from AllAwardEvents)
            List<AwardEvent> relevantAwardEvents = viewModel.AllAwardEvents.ToList();
            if (viewModel.LastCompletedAward != null && !relevantAwardEvents.Any(ae => ae.Id == viewModel.LastCompletedAward.Id))
            {
                relevantAwardEvents.Add(viewModel.LastCompletedAward);
            }

            if (!relevantAwardEvents.Any())
                return;

            // Get votes only for relevant award event IDs
            List<AwardVote> allVotes = await _awardVoteService.GetAllAsync();
            HashSet<Guid> relevantAwardEventIds = relevantAwardEvents.Select(ae => ae.Id).ToHashSet();
            List<AwardVote> relevantVotes = allVotes.Where(v => relevantAwardEventIds.Contains(v.AwardEventId)).ToList();

            // Use already-filtered ExistingEvents instead of fetching all events again
            List<MovieEvent> allEvents = viewModel.ExistingEvents;

            // Process results for each relevant award event and question combination
            foreach (AwardEvent awardEvent in relevantAwardEvents)
            {
                foreach (AwardQuestion question in viewModel.AllAwardQuestions.Where(q => awardEvent.Questions.Contains(q.Id)))
                {
                    List<QuestionResult> results = relevantVotes
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

        /// <summary>
        /// Ensures current month and next month exist in the database.
        /// Delegates creation to MovieEventService for proper separation of concerns.
        /// MovieEventService will automatically create Phase records if they don't exist.
        /// </summary>
        private async Task EnsureCurrentAndNextMonthExistAsync(HomePageViewModel viewModel)
        {
            DateTime currentMonth = DateProvider.Now.StartOfMonth();
            DateTime nextMonth = currentMonth.AddMonths(1);

            // Check if current month exists
            bool currentExists = viewModel.ExistingEvents.Any(e =>
                e.StartDate.Year == currentMonth.Year &&
                e.StartDate.Month == currentMonth.Month);

            if (!currentExists)
            {
                _logger.LogInformation("Creating missing event for current month: {Month:yyyy-MM}", currentMonth);
                MovieEvent? createdEvent = await _movieEventService.GetOrCreateForMonthAsync(currentMonth);
                if (createdEvent != null)
                {
                    viewModel.ExistingEvents.Add(createdEvent);
                    _logger.LogInformation("Successfully created event for {Month:yyyy-MM} → {Person}",
                        currentMonth, createdEvent.Person);
                }
                else
                {
                    _logger.LogInformation("Skipped event creation for {Month:yyyy-MM} (awards month)", currentMonth);
                }
            }

            // Check if next month exists
            bool nextExists = viewModel.ExistingEvents.Any(e =>
                e.StartDate.Year == nextMonth.Year &&
                e.StartDate.Month == nextMonth.Month);

            if (!nextExists)
            {
                _logger.LogInformation("Creating missing event for next month: {Month:yyyy-MM}", nextMonth);
                MovieEvent? createdEvent = await _movieEventService.GetOrCreateForMonthAsync(nextMonth);
                if (createdEvent != null)
                {
                    viewModel.ExistingEvents.Add(createdEvent);
                    _logger.LogInformation("Successfully created event for {Month:yyyy-MM} → {Person}",
                        nextMonth, createdEvent.Person);
                }
                else
                {
                    _logger.LogInformation("Skipped event creation for {Month:yyyy-MM} (awards month)", nextMonth);
                }
            }
        }
    }
}