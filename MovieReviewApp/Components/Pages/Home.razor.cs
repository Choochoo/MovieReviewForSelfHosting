using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MongoDB.Driver;
using MovieReviewApp.Extensions;
using MovieReviewApp.Models;
using MovieReviewApp.Services;

namespace MovieReviewApp.Components.Pages
{
    public partial class Home
    {
        [Inject]
        private IJSRuntime JS { get; set; } = default!;

        [Inject]
        private MovieReviewService movieReviewService { get; set; } = default!;

        [Inject]
        private DiscussionQuestionsService discussionQuestionsService { get; set; } = default!;

        private List<SiteUpdate> RecentUpdates { get; set; } = new();
        private bool showUpdates = true;
        public MovieEvent? CurrentEvent;
        public MovieEvent? NextEvent;
        private List<Phase> Phases { get; set; } = new();
        private readonly Random _rand = new(1337);

        // Properties for Razor page
        public List<DiscussionQuestion>? DiscussionQuestions { get; private set; }
        public bool IsCurrentPhaseAwardPhase { get; private set; }
        public AwardSetting? AwardSettings { get; private set; }

        // Cached data
        private List<Setting>? _settings;
        private DateTime? _startDate;
        private bool? _respectOrder;
        private string[]? _allNames;
        private List<MovieEvent>? _existingEvents;
        private List<Phase>? _dbPhases;

        private bool _isInitialized = false;

        protected override async Task OnInitializedAsync()
        {
            try
            {
                // Load all data asynchronously at initialization
                await LoadAllDataAsync();

                // Load discussion questions
                DiscussionQuestions = await discussionQuestionsService.GetActiveQuestionsAsync();

                // Check if current phase is award phase
                var currentAwardEvent = await movieReviewService.GetAwardEventForDateAsync(DateProvider.Now);
                IsCurrentPhaseAwardPhase = currentAwardEvent != null;

                // Load award settings
                AwardSettings = await movieReviewService.GetAwardSettingsAsync();

                // Generate schedule if we have the required data
                if (_allNames != null && _allNames.Length > 0 && _startDate.HasValue && _startDate.Value != DateTime.MinValue)
                {
                    await GenerateScheduleAsync(_startDate.Value, _allNames);
                }
                else
                {
                    CurrentEvent = null;
                    NextEvent = null;
                }

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in OnInitializedAsync: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private async Task LoadAllDataAsync()
        {
            // Load settings
            _settings = await movieReviewService.GetSettingsAsync();

            // Parse start date
            var startDateSetting = _settings?.FirstOrDefault(x => x.Key == "StartDate")?.Value;
            if (!string.IsNullOrEmpty(startDateSetting) && DateTime.TryParse(startDateSetting, out var date))
            {
                _startDate = date;
            }
            else
            {
                _startDate = DateTime.MinValue;
            }

            // Parse respect order
            var respectOrderSetting = _settings?.FirstOrDefault(x => x.Key == "RespectOrder");
            _respectOrder = respectOrderSetting != null &&
                           !string.IsNullOrEmpty(respectOrderSetting.Value) &&
                           bool.TryParse(respectOrderSetting.Value, out var respect) &&
                           respect;

            // Load people names
            var people = await movieReviewService.GetAllPeopleAsync(_respectOrder.Value);
            _allNames = people.Select(x => x.Name)
                .Where(x => !string.IsNullOrEmpty(x))
                .ToArray();

            // Load existing events
            _existingEvents = (await movieReviewService.GetAllMovieEventsAsync()).ToList();

            // Load phases
            _dbPhases = (await movieReviewService.GetAllPhasesAsync()).OrderBy(p => p.StartDate).ToList();

            // Advance random number generator based on existing events
            if (_existingEvents != null)
            {
                for (int i = 0; i < _existingEvents.Count; i++)
                {
                    _rand.Next(_allNames?.Length ?? 1);
                }
            }
        }

        private async Task GenerateScheduleAsync(DateTime startDate, string[] allNames)
        {
            if (_dbPhases == null || AwardSettings == null) return;

            foreach (var phase in _dbPhases)
            {
                // Generate events for this phase
                var generatedPhase = await GeneratePhaseAsync(phase.Number, phase.StartDate, allNames.ToList());
                Phases.Add(generatedPhase);

                // If we're in this phase's time period
                if (DateProvider.Now.IsWithinRange(phase.StartDate, phase.EndDate))
                {
                    UpdateCurrentAndNextEvents(generatedPhase);
                }

                // If we're in an award month after this phase
                if (phase.Number % AwardSettings.PhasesBeforeAward == 0)
                {
                    var awardDate = phase.EndDate.AddDays(1);
                    var awardMonthEnd = awardDate.AddMonths(1).AddDays(-1);

                    if (DateProvider.Now.IsWithinRange(awardDate, awardMonthEnd))
                    {
                        CurrentEvent = null;
                        var nextPhase = _dbPhases.FirstOrDefault(p => p.Number == phase.Number + 1);
                        if (nextPhase != null)
                        {
                            var nextGeneratedPhase = await GeneratePhaseAsync(nextPhase.Number, nextPhase.StartDate, allNames.ToList());
                            NextEvent = nextGeneratedPhase.Events.FirstOrDefault();
                        }
                        else
                        {
                            // Create a new phase if it doesn't exist yet
                            var newPhaseNumber = phase.Number + 1;
                            var newPhaseStartDate = awardMonthEnd.AddDays(1);
                            var newGeneratedPhase = await GeneratePhaseAsync(newPhaseNumber, newPhaseStartDate, allNames.ToList());
                            Phases.Add(newGeneratedPhase);
                            NextEvent = newGeneratedPhase.Events.FirstOrDefault();
                        }
                    }
                }
            }

            // Generate additional future phases for display purposes
            if (Phases.Any() && AwardSettings != null)
            {
                var lastPhase = Phases.OrderByDescending(p => p.Number).First();
                var additionalPhasesToGenerate = 2;

                for (int i = 1; i <= additionalPhasesToGenerate; i++)
                {
                    var newPhaseNumber = lastPhase.Number + i;
                    DateTime newPhaseStart;

                    if (lastPhase.Number % AwardSettings.PhasesBeforeAward == 0)
                    {
                        var awardMonthEnd = lastPhase.EndDate.AddDays(1).AddMonths(1).AddDays(-1);
                        newPhaseStart = awardMonthEnd.AddDays(1);
                    }
                    else
                    {
                        newPhaseStart = lastPhase.EndDate.AddDays(1);
                    }

                    var futurePhase = await GeneratePhaseAsync(newPhaseNumber, newPhaseStart, allNames.ToList());
                    Phases.Add(futurePhase);
                }
            }
        }

        private async Task<Phase> GeneratePhaseAsync(int phaseNumber, DateTime startDate, List<string> peopleNames)
        {
            var phase = new Phase
            {
                Number = phaseNumber,
                StartDate = startDate,
                EndDate = startDate.AddMonths(peopleNames.Count).EndOfDay(),
                Events = new List<MovieEvent>(),
                People = string.Join(',', peopleNames)
            };

            var currentDate = startDate;
            var availablePeople = new List<string>(peopleNames);

            // Get existing events from cached events
            var existingPhaseEvents = _existingEvents?.Where(e => e.PhaseNumber == phaseNumber) ?? Enumerable.Empty<MovieEvent>();

            foreach (var existingEvent in existingPhaseEvents)
            {
                // Set default MeetupTime if null
                if (!existingEvent.MeetupTime.HasValue)
                {
                    existingEvent.MeetupTime = existingEvent.StartDate.StartOfMonth().LastFridayOfMonth().AddHours(18);
                }
                phase.Events.Add(existingEvent);
                availablePeople.Remove(existingEvent.Person);
                currentDate = existingEvent.EndDate.AddDays(1);
            }

            while (availablePeople.Any())
            {
                var personIndex = _respectOrder.GetValueOrDefault() ? 0 : _rand.Next(availablePeople.Count);
                var person = availablePeople[personIndex];

                phase.Events.Add(new MovieEvent
                {
                    StartDate = currentDate,
                    EndDate = currentDate.EndOfMonth(),
                    Person = person,
                    PhaseNumber = phaseNumber,
                    FromDatabase = false,
                    IsEditing = false,
                    MeetupTime = currentDate.StartOfMonth().LastFridayOfMonth().AddHours(18)
                });

                availablePeople.Remove(person);
                currentDate = currentDate.AddMonths(1);
            }

            return phase;
        }

        private void UpdateCurrentAndNextEvents(Phase phase)
        {
            CurrentEvent = phase.Events
                .FirstOrDefault(e => DateProvider.Now.IsWithinRange(e.StartDate, e.EndDate));

            var nextMonthDate = DateProvider.Now.AddMonths(1);
            NextEvent = phase.Events
                .FirstOrDefault(e => nextMonthDate.IsWithinRange(e.StartDate, e.EndDate));
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender && _isInitialized)
            {
                try
                {
                    var lastVisitStr = await JS.InvokeAsync<string>("localStorage.getItem", "lastVisit");
                    var lastVisit = string.IsNullOrEmpty(lastVisitStr)
                        ? DateTime.UtcNow.AddDays(-1)
                        : DateTime.Parse(lastVisitStr);

                    var updates = await movieReviewService.GetRecentUpdatesAsync(lastVisit);
                    await JS.InvokeVoidAsync("localStorage.setItem", "lastVisit", DateTime.UtcNow.ToString("o"));

                    // Only call StateHasChanged if we actually have updates to show
                    if (updates.Any() && !RecentUpdates.Any())
                    {
                        RecentUpdates = updates;
                        StateHasChanged();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in OnAfterRenderAsync: {ex.Message}");
                }
            }
        }

        private async Task DismissUpdates()
        {
            showUpdates = false;
            await JS.InvokeVoidAsync("localStorage.setItem", "lastVisit", DateTime.UtcNow.ToString("o"));
        }

        public List<string> GetEligibleMoviesForPhase(int phaseNumber)
        {
            if (_existingEvents == null || AwardSettings == null) return new List<string>();

            return _existingEvents
                .Where(m => m.PhaseNumber <= phaseNumber &&
                           m.PhaseNumber > phaseNumber - AwardSettings.PhasesBeforeAward &&
                           !string.IsNullOrEmpty(m.Movie))
                .Select(m => m.Movie)
                .ToList();
        }

        public AwardEvent? GetPreviousAwardEvent()
        {
            try
            {
                // Return null if not initialized or missing required data
                if (!_isInitialized || !_startDate.HasValue || _dbPhases == null ||
                    _dbPhases.Count == 0 || AwardSettings == null)
                {
                    return null;
                }

                if (DateProvider.Now < _startDate.Value)
                {
                    return null;
                }

                // Find the current phase we're in
                var currentPhase = _dbPhases.FirstOrDefault(p =>
                    DateProvider.Now.IsWithinRange(p.StartDate, p.EndDate));

                if (currentPhase == null)
                {
                    return null;
                }

                // If the previous phase was an award phase
                var previousPhaseNumber = currentPhase.Number - 1;

                if (previousPhaseNumber > 0 && previousPhaseNumber % AwardSettings.PhasesBeforeAward == 0)
                {
                    var previousPhase = _dbPhases.FirstOrDefault(p => p.Number == previousPhaseNumber);
                    if (previousPhase == null)
                    {
                        return null;
                    }

                    var awardMonthStart = previousPhase.EndDate.AddDays(1);
                    var awardMonthEnd = awardMonthStart.AddMonths(1).AddDays(-1);

                    // This would need to be async in a real implementation
                    // For now, return null to avoid blocking
                    // You should create an async version of this method
                    return null;
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetPreviousAwardEvent: {ex.Message}");
                return null;
            }
        }
    }
}