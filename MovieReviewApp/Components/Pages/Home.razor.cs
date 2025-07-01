using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MongoDB.Driver;
using MovieReviewApp.Extensions;
using MovieReviewApp.Models;
using MovieReviewApp.Models.ViewModels;
using MovieReviewApp.Application.Services;
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

        private HomePageViewModel? _viewModel;
        private List<Phase> Phases { get; set; } = new();
        private readonly Random _rand = new(1337);
        private bool showUpdates = true;
        private bool _isInitialized = false;

        // Exposed properties for Razor page
        public List<SiteUpdate> RecentUpdates => _viewModel?.RecentUpdates ?? new();
        public MovieEvent? CurrentEvent => _viewModel?.CurrentEvent;
        public MovieEvent? NextEvent => _viewModel?.NextEvent;
        public bool IsShowingPastEvent => _viewModel?.IsShowingPastEvent ?? false;
        public List<DiscussionQuestion> DiscussionQuestions => _viewModel?.DiscussionQuestions ?? new();
        public bool IsCurrentPhaseAwardPhase => _viewModel?.IsCurrentPhaseAwardPhase ?? false;
        public AwardSetting? AwardSettings => _viewModel?.AwardSettings;
        public List<AwardEvent> AllAwardEvents => _viewModel?.AllAwardEvents ?? new();
        public List<AwardQuestion> AllAwardQuestions => _viewModel?.AllAwardQuestions ?? new();
        public List<Person> AllPeople => _viewModel?.AllPeople ?? new();
        public Dictionary<(Guid, Guid), List<QuestionResult>> CachedResults => _viewModel?.QuestionResults ?? new();

        /// <summary>
        /// Initializes the component by loading all required data asynchronously.
        /// </summary>
        protected override async Task OnInitializedAsync()
        {
            // Load all data in a single operation
            _viewModel = await HomePageDataService.GetHomePageDataAsync();

            // Advance random number generator based on existing event count
            for (int i = 0; i < _viewModel.ExistingEventCount; i++)
            {
                _rand.Next(_viewModel.AllNames?.Length ?? 1);
            }

            // Generate schedule if we have the required data
            if (_viewModel.AllNames.Length > 0 && _viewModel.StartDate.HasValue && _viewModel.StartDate.Value != DateTime.MinValue)
            {
                await GenerateScheduleAsync(_viewModel.StartDate.Value, _viewModel.AllNames);
            }

            _isInitialized = true;
        }


        /// <summary>
        /// Generates the movie schedule based on phases and award settings.
        /// </summary>
        private async Task GenerateScheduleAsync(DateTime startDate, string[] allNames)
        {
            if (_viewModel?.DbPhases == null || AwardSettings == null) return;

            foreach (var phase in _viewModel.DbPhases)
            {
                // Generate events for this phase
                Phase generatedPhase = await GeneratePhaseAsync(phase.Number, phase.StartDate, allNames.ToList());
                Phases.Add(generatedPhase);

                // If we're in this phase's time period
                if (DateProvider.Now.IsWithinRange(phase.StartDate, phase.EndDate))
                {
                    UpdateCurrentAndNextEvents(generatedPhase);
                }

                // If we're in an award month after this phase
                if (phase.Number % AwardSettings.PhasesBeforeAward == 0)
                {
                    DateTime awardDate = phase.EndDate.AddDays(1);
                    DateTime awardMonthEnd = awardDate.AddMonths(1).AddDays(-1);

                    if (DateProvider.Now.IsWithinRange(awardDate, awardMonthEnd))
                    {
                        _viewModel.CurrentEvent = null;
                        Phase? nextPhase = _viewModel.DbPhases.FirstOrDefault(p => p.Number == phase.Number + 1);
                        if (nextPhase != null)
                        {
                            Phase nextGeneratedPhase = await GeneratePhaseAsync(nextPhase.Number, nextPhase.StartDate, allNames.ToList());
                            _viewModel.NextEvent = nextGeneratedPhase.Events.FirstOrDefault();
                        }
                        else
                        {
                            // Create a new phase if it doesn't exist yet
                            int newPhaseNumber = phase.Number + 1;
                            DateTime newPhaseStartDate = awardMonthEnd.AddDays(1);
                            Phase newGeneratedPhase = await GeneratePhaseAsync(newPhaseNumber, newPhaseStartDate, allNames.ToList());
                            Phases.Add(newGeneratedPhase);
                            _viewModel.NextEvent = newGeneratedPhase.Events.FirstOrDefault();
                        }
                    }
                }
            }

            // Generate additional future phases for display purposes
            if (Phases.Any() && AwardSettings != null)
            {
                Phase lastPhase = Phases.OrderByDescending(p => p.Number).First();
                int additionalPhasesToGenerate = 2;

                for (int i = 1; i <= additionalPhasesToGenerate; i++)
                {
                    int newPhaseNumber = lastPhase.Number + i;
                    DateTime newPhaseStart;

                    if (lastPhase.Number % AwardSettings.PhasesBeforeAward == 0)
                    {
                        DateTime awardMonthEnd = lastPhase.EndDate.AddDays(1).AddMonths(1).AddDays(-1);
                        newPhaseStart = awardMonthEnd.AddDays(1);
                    }
                    else
                    {
                        newPhaseStart = lastPhase.EndDate.AddDays(1);
                    }

                    Phase futurePhase = await GeneratePhaseAsync(newPhaseNumber, newPhaseStart, allNames.ToList());
                    Phases.Add(futurePhase);
                }
            }
        }

        private async Task<Phase> GeneratePhaseAsync(int phaseNumber, DateTime startDate, List<string> peopleNames)
        {
            Phase phase = CreatePhaseStructure(phaseNumber, startDate, peopleNames);
            AddExistingEventsToPhase(phase, phaseNumber);
            AddMissingEventsToPhase(phase, peopleNames);
            return phase;
        }

        private Phase CreatePhaseStructure(int phaseNumber, DateTime startDate, List<string> peopleNames)
        {
            return new Phase
            {
                Number = phaseNumber,
                StartDate = startDate,
                EndDate = startDate.AddMonths(peopleNames.Count).EndOfDay(),
                Events = new List<MovieEvent>(),
                People = string.Join(',', peopleNames)
            };
        }

        private void AddExistingEventsToPhase(Phase phase, int phaseNumber)
        {
            IEnumerable<MovieEvent> existingEvents = _viewModel?.ExistingEvents?.Where(e => e.PhaseNumber == phaseNumber) ?? Enumerable.Empty<MovieEvent>();
            foreach (var existingEvent in existingEvents)
            {
                PhaseEventGenerator.SetDefaultMeetupTime(existingEvent);
                phase.Events.Add(existingEvent);
            }
        }

        private void AddMissingEventsToPhase(Phase phase, List<string> peopleNames)
        {
            List<string> usedPeople = phase.Events.Select(e => e.Person).ToList();
            List<string> availablePeople = peopleNames.Except(usedPeople).ToList();
            DateTime currentMonth = CalculateNextAvailableMonth(phase);

            while (availablePeople.Any())
            {
                int personIndex = _viewModel?.RespectOrder == true ? 0 : _rand.Next(availablePeople.Count);
                string person = availablePeople[personIndex];

                MovieEvent newEvent = PhaseEventGenerator.CreateMovieEvent(person, currentMonth, phase.Number);
                phase.Events.Add(newEvent);
                
                availablePeople.Remove(person);
                currentMonth = currentMonth.AddMonths(1);
            }
        }

        private DateTime CalculateNextAvailableMonth(Phase phase)
        {
            return phase.Events.Any() 
                ? phase.Events.Max(e => e.EndDate).AddDays(1).Date 
                : phase.StartDate;
        }

        private void UpdateCurrentAndNextEvents(Phase phase)
        {
            if (_viewModel == null) return;
            
            _viewModel.CurrentEvent = phase.Events
                .FirstOrDefault(e => DateProvider.Now.IsWithinRange(e.StartDate, e.EndDate));

            DateTime nextMonthDate = DateProvider.Now.AddMonths(1);
            _viewModel.NextEvent = phase.Events
                .FirstOrDefault(e => nextMonthDate.IsWithinRange(e.StartDate, e.EndDate));
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
                .Select(m => m.Movie)
                .ToList();
        }

        // Removed GetPreviousAwardEvent() to prevent async deadlocks
        // Award events are now displayed in the chronological timeline


        // Method to create chronological timeline with phases and award events
        /// <summary>
        /// Creates a chronological timeline combining phases and award events.
        /// </summary>
        public List<ITimelineItem> GetChronologicalTimeline()
        {
            List<ITimelineItem> timeline = new List<ITimelineItem>();
            
            if (Phases == null || AwardSettings == null) return timeline;

            // Add all phases to timeline (including past ones)
            foreach (Phase phase in Phases)
            {
                timeline.Add(new PhaseTimelineItem(phase));
                
                // Check if this phase should have an award event after it
                if (phase.Number % AwardSettings.PhasesBeforeAward == 0)
                {
                    DateTime awardDate = phase.EndDate.AddDays(1);
                    
                    // Look for an existing award event for this time period
                    AwardEvent? awardEvent = AllAwardEvents.FirstOrDefault(ae => 
                        ae.StartDate >= awardDate && ae.StartDate <= awardDate.AddMonths(1));
                    
                    if (awardEvent != null)
                    {
                        timeline.Add(new AwardTimelineItem(awardEvent));
                    }
                    else if (awardDate > DateProvider.Now)
                    {
                        // Only create placeholder for future award events
                        FutureAwardItem futureAward = new FutureAwardItem 
                        { 
                            PhaseNumber = phase.Number, 
                            AwardDate = awardDate 
                        };
                        timeline.Add(new FutureAwardTimelineItem(futureAward));
                    }
                }
            }
            
            // Also add any standalone award events that might not have been matched to phases
            foreach (AwardEvent awardEvent in AllAwardEvents)
            {
                if (!timeline.OfType<AwardTimelineItem>().Any(t => t.AwardEvent.Id == awardEvent.Id))
                {
                    timeline.Add(new AwardTimelineItem(awardEvent));
                }
            }
            
            // Sort by date (oldest first for chronological display)
            return timeline.OrderBy(t => t.Date).ToList();
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