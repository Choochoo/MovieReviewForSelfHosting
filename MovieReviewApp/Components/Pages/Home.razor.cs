using System.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MovieReviewApp.Application.Services;
using MovieReviewApp.Extensions;
using MovieReviewApp.Models;
using MovieReviewApp.Models.ViewModels;
using MovieReviewApp.Utilities;

namespace MovieReviewApp.Components.Pages
{
    public partial class Home : ComponentBase
    {
        [Inject]
        private IJSRuntime JS { get; set; } = default!;

        [Inject]
        private HomePageDataService HomePageDataService { get; set; } = default!;

        [Inject]
        private SiteUpdateService SiteUpdateService { get; set; } = default!;

        [Inject]
        private PersonAssignmentCacheService PersonAssignmentCache { get; set; } = default!;

        [Inject]
        private TimelineRenderingService TimelineRenderingService { get; set; } = default!;

        [Inject]
        private MovieEventService MovieEventService { get; set; } = default!;

        private HomePageViewModel? _viewModel;
        private List<Phase> Phases { get; set; } = new();
        // Random number generation is now handled by PersonRotationService
        private bool showUpdates = true;
        private bool _isInitialized = false;
        private TimelineViewModel? _structuredTimeline;

        // Exposed properties for Razor page
        public List<SiteUpdate> RecentUpdates => _viewModel?.RecentUpdates ?? new();
        public MovieEvent? CurrentEvent => _viewModel?.CurrentEvent;
        public MovieEvent? NextEvent => _viewModel?.NextEvent;
        public bool IsShowingPastEvent => _viewModel?.IsShowingPastEvent ?? false;
        public List<DiscussionQuestion> DiscussionQuestions => _viewModel?.DiscussionQuestions ?? new();
        public bool IsCurrentPhaseAwardPhase => _viewModel?.IsCurrentPhaseAwardPhase ?? false;
        public AwardSetting? AwardSettings => _viewModel?.AwardSettings;
        public List<AwardEvent> AllAwardEvents => _viewModel?.AllAwardEvents ?? new();
        public AwardEvent? LastCompletedAward => _viewModel?.LastCompletedAward;
        public List<AwardQuestion> AllAwardQuestions => _viewModel?.AllAwardQuestions ?? new();
        public List<Person> AllPeople => _viewModel?.AllPeople ?? new();
        public Dictionary<(Guid, Guid), List<QuestionResult>> CachedResults => _viewModel?.QuestionResults ?? new();
        public TimelineViewModel? StructuredTimeline => _structuredTimeline;

        /// <summary>
        /// Initializes the component by loading all required data asynchronously.
        /// </summary>
        protected override async Task OnInitializedAsync()
        {
            Console.WriteLine("Home.razor.cs: OnInitializedAsync started");

            // Load all data in a single operation
            _viewModel = await HomePageDataService.GetHomePageDataAsync();
            Console.WriteLine("Home.razor.cs: Data loaded");

            // Load structured timeline directly (no conversion needed)
            _structuredTimeline = await TimelineRenderingService.BuildTimelineAsync();
            Console.WriteLine("Home.razor.cs: Timeline loaded");

            // Add a delay to make the fade effect visible (adjust as needed)
            await Task.Delay(500); // 0.5 second delay
            Console.WriteLine("Home.razor.cs: Delay completed");

            // Ensure we're on the UI thread and trigger a single state change
            await InvokeAsync(() =>
            {
                _isInitialized = true;
                Console.WriteLine("Home.razor.cs: _isInitialized set to true");
                StateHasChanged();
            });
        }



        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender && _isInitialized)
            {
                try
                {
                    string lastVisitStr = await JS.InvokeAsync<string>("localStorage.getItem", "lastVisit");
                    DateTime lastVisit = string.IsNullOrEmpty(lastVisitStr)
                        ? DateTime.UtcNow.AddDays(-1)
                        : DateTime.Parse(lastVisitStr);

                    // If we didn't load recent updates in the initial data fetch, load them now
                    if (_viewModel != null && !_viewModel.RecentUpdates.Any())
                    {
                        List<SiteUpdate> updates = await SiteUpdateService.GetRecentUpdatesAsync(lastVisit);
                        if (updates.Any())
                        {
                            _viewModel.RecentUpdates = updates;
                            StateHasChanged();
                        }
                    }

                    await JS.InvokeVoidAsync("localStorage.setItem", "lastVisit", DateTime.UtcNow.ToString("o"));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in OnAfterRenderAsync: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets movies eligible for awards for the specified phase.
        /// </summary>
        public List<string> GetEligibleMoviesForPhase(int phaseNumber)
        {
            if (_viewModel?.ExistingEvents == null || AwardSettings == null) return new List<string>();

            return _viewModel.ExistingEvents
                .Where(m => m.PhaseNumber <= phaseNumber &&
                           m.PhaseNumber > phaseNumber - AwardSettings.PhasesBeforeAward &&
                           !string.IsNullOrEmpty(m.Movie))
                .Select(m => m.Movie!)
                .ToList();
        }


        // Dictionary to track which award results are being shown
        private Dictionary<string, bool> showResultsDict = new Dictionary<string, bool>();

        public bool IsShowingResults(Guid awardEventId, Guid questionId) =>
            showResultsDict.ContainsKey($"show_{awardEventId}_{questionId}") && showResultsDict[$"show_{awardEventId}_{questionId}"];

        public void ToggleResults(Guid awardEventId, Guid questionId)
        {
            string key = $"show_{awardEventId}_{questionId}";
            if (!showResultsDict.ContainsKey(key))
                showResultsDict[key] = false;

            showResultsDict[key] = !showResultsDict[key];
            StateHasChanged();
        }
    }
}