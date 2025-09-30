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

        private HomePageViewModel? _viewModel;
        private List<Phase> Phases { get; set; } = new();
        // Random number generation is now handled by PersonRotationService
        private bool showUpdates = true;
        private bool _isInitialized = false;
        private List<ITimelineItem> _chronologicalTimeline = new();

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
        public List<ITimelineItem> ChronologicalTimeline => _chronologicalTimeline;

        /// <summary>
        /// Initializes the component by loading all required data asynchronously.
        /// </summary>
        protected override async Task OnInitializedAsync()
        {
            Console.WriteLine("Home.razor.cs: OnInitializedAsync started");

            // Load all data in a single operation
            _viewModel = await HomePageDataService.GetHomePageDataAsync();
            Console.WriteLine("Home.razor.cs: Data loaded");

            // Generate chronological timeline after all data is loaded
            _chronologicalTimeline = await GetChronologicalTimeline();
            Console.WriteLine("Home.razor.cs: Timeline generated");

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


        // Method to create chronological timeline using cache-first architecture
        /// <summary>
        /// Builds chronological timeline for current and future events.
        /// Uses cache-first architecture: PersonAssignmentCache is source of truth,
        /// database MovieEvents provide enrichment. No Phase table dependency.
        /// Only shows current and future events (from current month onwards).
        /// </summary>
        public async Task<List<ITimelineItem>> GetChronologicalTimeline()
        {
            List<ITimelineItem> timeline = new List<ITimelineItem>();

            if (AwardSettings == null) return timeline;

            // Add last completed award at the top (if it exists)
            if (LastCompletedAward != null)
            {
                timeline.Add(new AwardTimelineItem(LastCompletedAward));
            }

            try
            {
                // Use new cache-first timeline service (fixes phases.count == 0 bug)
                TimelineViewModel timelineData = await TimelineRenderingService.BuildTimelineAsync();

                Console.WriteLine($"[GetChronologicalTimeline] TimelineData received:");
                Console.WriteLine($"  - CurrentPhase: {timelineData.CurrentPhase != null}");
                Console.WriteLine($"  - FuturePhases: {timelineData.FuturePhases.Count}");
                Console.WriteLine($"  - PastPhases: {timelineData.PastPhases.Count}");

                // Convert new structure back to existing ITimelineItem structure
                // (keeps UI rendering unchanged for Phase 1)

                // Add current phase if exists
                if (timelineData.CurrentPhase != null)
                {
                    Phase currentPhase = ConvertTimelinePhaseToPhase(timelineData.CurrentPhase);
                    Console.WriteLine($"  - Current Phase converted: {currentPhase.Events.Count} events");
                    timeline.Add(new PhaseTimelineItem(currentPhase));
                }

                // Add future phases
                int futurePhaseIndex = 0;
                foreach (TimelinePhase futurePhase in timelineData.FuturePhases)
                {
                    futurePhaseIndex++;
                    Console.WriteLine($"  - Processing Future Phase {futurePhaseIndex}/{timelineData.FuturePhases.Count}: Phase {futurePhase.PhaseNumber}, {futurePhase.Items.Count} items");
                    // Check for awards events in this phase
                    MovieReviewApp.Models.ViewModels.TimelineItem? awardsItem = futurePhase.Items
                        .FirstOrDefault(i => i.IsAwardsEvent);

                    if (awardsItem != null)
                    {
                        // Look for existing AwardEvent in database
                        AwardEvent? existingAward = AllAwardEvents
                            .FirstOrDefault(ae => ae.StartDate.Year == awardsItem.Month.Year &&
                                                 ae.StartDate.Month == awardsItem.Month.Month);

                        if (existingAward != null)
                        {
                            timeline.Add(new AwardTimelineItem(existingAward));
                        }
                        else
                        {
                            // Create future award placeholder
                            FutureAwardItem futureAward = new FutureAwardItem
                            {
                                PhaseNumber = futurePhase.PhaseNumber,
                                AwardDate = awardsItem.Month
                            };
                            timeline.Add(new FutureAwardTimelineItem(futureAward));
                        }
                    }
                    else
                    {
                        // Regular phase (not awards)
                        Phase phase = ConvertTimelinePhaseToPhase(futurePhase);
                        Console.WriteLine($"    - Converted to Phase with {phase.Events.Count} events");

                        if (phase.Events.Count > 0)
                        {
                            timeline.Add(new PhaseTimelineItem(phase));
                            Console.WriteLine($"    - Added to timeline as PhaseTimelineItem");
                        }
                        else
                        {
                            Console.WriteLine($"    - SKIPPED (0 events) - THIS IS THE BUG!");

                            // Add cache-only items as FuturePersonTimelineItem instead
                            foreach (MovieReviewApp.Models.ViewModels.TimelineItem cacheItem in futurePhase.Items.Where(i => !i.IsAwardsEvent))
                            {
                                timeline.Add(new FuturePersonTimelineItem(cacheItem.AssignedPersonName, cacheItem.Month));
                                Console.WriteLine($"      - Added {cacheItem.Month:yyyy-MM} ({cacheItem.AssignedPersonName}) as FuturePersonTimelineItem");
                            }
                        }
                    }
                }

                Console.WriteLine($"[GetChronologicalTimeline] Final timeline has {timeline.Count} items");
            }
            catch (Exception ex)
            {
                // Fallback to empty timeline if cache-first fails
                Console.WriteLine($"Error building cache-first timeline: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                // Timeline will be empty but won't crash the page
            }

            // Sort by date (oldest first for chronological display)
            List<ITimelineItem> sorted = timeline.OrderBy(t => t.Date).ToList();
            Console.WriteLine($"[GetChronologicalTimeline] Returning {sorted.Count} sorted timeline items");
            return sorted;
        }

        /// <summary>
        /// Converts new TimelinePhase structure to legacy Phase structure for UI compatibility.
        /// </summary>
        private Phase ConvertTimelinePhaseToPhase(TimelinePhase timelinePhase)
        {
            // Build MovieEvent list from TimelineItems that have database records
            List<MovieEvent> events = new List<MovieEvent>();

            foreach (MovieReviewApp.Models.ViewModels.TimelineItem item in timelinePhase.Items.Where(i => !i.IsAwardsEvent))
            {
                if (item.MovieEventId.HasValue && _viewModel?.ExistingEvents != null)
                {
                    MovieEvent? dbEvent = _viewModel.ExistingEvents
                        .FirstOrDefault(e => e.Id == item.MovieEventId.Value);

                    if (dbEvent != null)
                    {
                        events.Add(dbEvent);
                    }
                }
            }

            // Get people names from AllPeople ordered by Order field
            List<string> peopleNames = AllPeople
                .OrderBy(p => p.Order)
                .Select(p => p.Name!)
                .ToList();

            return new Phase
            {
                Number = timelinePhase.PhaseNumber,
                StartDate = timelinePhase.StartMonth,
                EndDate = timelinePhase.EndMonth,
                People = string.Join(", ", peopleNames),
                Events = events
            };
        }

        /// <summary>
        /// Adds future cached assignments from PersonAssignmentCache to the timeline.
        /// Creates timeline items for future months without touching the database.
        /// </summary>
        private async Task AddFutureCachedEventsToTimeline(List<ITimelineItem> timeline, DateTime startMonth)
        {
            Console.WriteLine($"[AddFutureCachedEventsToTimeline] Called with startMonth: {startMonth:yyyy-MM}");

            // Get entire cache in ONE call (15,000× faster than 240 individual async calls)
            IReadOnlyDictionary<DateTime, string> allAssignments =
                await PersonAssignmentCache.GetAllAssignmentsAsync();

            // Build HashSet of existing database event months for O(1) lookups
            // Database events ALWAYS take priority over cache
            HashSet<DateTime> existingEventMonths = new HashSet<DateTime>(
                _viewModel?.ExistingEvents?.Select(e => e.StartDate.StartOfMonth()) ?? Enumerable.Empty<DateTime>()
            );

            Console.WriteLine($"[AddFutureCachedEventsToTimeline] Found {existingEventMonths.Count} existing database events");
            Console.WriteLine($"[AddFutureCachedEventsToTimeline] Cache contains {allAssignments.Count} assignments");

            // Show future 12 months from cache (only months NOT in database)
            DateTime endMonth = startMonth.AddMonths(12);
            int addedCount = 0;

            for (DateTime month = startMonth; month <= endMonth; month = month.AddMonths(1))
            {
                DateTime monthKey = month.StartOfMonth();

                // Skip if database already has an event for this month (DATABASE FIRST!)
                if (existingEventMonths.Contains(monthKey))
                {
                    Console.WriteLine($"[AddFutureCachedEventsToTimeline] Skipping {month:yyyy-MM} - exists in database");
                    continue;
                }

                // Use cache ONLY for months not in database
                if (allAssignments.TryGetValue(monthKey, out string? assignment))
                {
                    if (assignment.StartsWith("Awards Event"))
                    {
                        timeline.Add(new FutureAwardTimelineItem(new FutureAwardItem
                        {
                            AwardDate = month
                        }));
                        Console.WriteLine($"[AddFutureCachedEventsToTimeline] Added future award: {month:yyyy-MM}");
                    }
                    else
                    {
                        timeline.Add(new FuturePersonTimelineItem(assignment, month));
                        Console.WriteLine($"[AddFutureCachedEventsToTimeline] Added future person: {month:yyyy-MM} -> {assignment}");
                    }
                    addedCount++;
                }
            }

            Console.WriteLine($"[AddFutureCachedEventsToTimeline] Added {addedCount} future items from cache");
        }

        /// <summary>
        /// Creates a Phase object from a list of MovieEvents.
        /// </summary>
        private Phase CreatePhaseFromEvents(int phaseNumber, DateTime startDate, List<MovieEvent> events, string people)
        {
            return new Phase
            {
                Number = phaseNumber,
                StartDate = startDate.StartOfMonth(),
                EndDate = events.Any() ? events.Max(e => e.EndDate) : startDate.EndOfMonth(),
                Events = new List<MovieEvent>(events),
                People = people
            };
        }

        /// <summary>
        /// Determines the next phase number for future phases.
        /// </summary>
        private int GetNextPhaseNumber()
        {
            if (Phases == null || !Phases.Any()) return 1;
            return Phases.Max(p => p.Number) + 1;
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