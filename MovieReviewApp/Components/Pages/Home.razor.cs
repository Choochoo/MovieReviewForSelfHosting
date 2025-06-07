using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MongoDB.Driver;
using MovieReviewApp.Extensions;
using MovieReviewApp.Models;
using MovieReviewApp.Services;

namespace MovieReviewApp.Components.Pages
{
    public partial class Home : ComponentBase
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
        public List<AwardEvent> AllAwardEvents { get; private set; } = new();
        public List<AwardQuestion> AllAwardQuestions { get; private set; } = new();
        public List<Person> AllPeople { get; private set; } = new();
        public Dictionary<(Guid, Guid), List<QuestionResult>> CachedResults { get; private set; } = new();

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
            // Load all data asynchronously at initialization
            await LoadAllDataAsync();

            // Load discussion questions
            DiscussionQuestions = await discussionQuestionsService.GetActiveQuestionsAsync();

            // Check if current phase is award phase
            var currentAwardEvent = await movieReviewService.GetAwardEventForDateAsync(DateProvider.Now);
            IsCurrentPhaseAwardPhase = currentAwardEvent != null;

            // Load award settings
            AwardSettings = await movieReviewService.GetAwardSettingsAsync();

            // Load all award events, questions, and people for chronological display
            var awardEventsTask = movieReviewService.GetAwardEventsAsync();
            var awardQuestionsTask = movieReviewService.GetActiveAwardQuestionsAsync();
            var peopleTask = movieReviewService.GetAllPeopleAsync(true);

            await Task.WhenAll(awardEventsTask, awardQuestionsTask, peopleTask);
            
            AllAwardEvents = await awardEventsTask;
            AllAwardQuestions = await awardQuestionsTask;
            AllPeople = await peopleTask;

            // Preload all question results to avoid sync calls in UI
            await PreloadQuestionResultsAsync();

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

        private async Task LoadAllDataAsync()
        {
            // Load settings
            _settings = await movieReviewService.GetAllSettingsAsync();

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

        // Removed GetPreviousAwardEvent() to prevent async deadlocks
        // Award events are now displayed in the chronological timeline

        private async Task PreloadQuestionResultsAsync()
        {
            var resultTasks = new List<Task>();
            
            foreach (var awardEvent in AllAwardEvents)
            {
                foreach (var question in AllAwardQuestions.Where(q => awardEvent.Questions.Contains(q.Id)))
                {
                    var task = LoadQuestionResultAsync(awardEvent.Id, question.Id);
                    resultTasks.Add(task);
                }
            }
            
            await Task.WhenAll(resultTasks);
        }

        private async Task LoadQuestionResultAsync(Guid awardEventId, Guid questionId)
        {
            try
            {
                var results = await movieReviewService.GetQuestionResultsAsync(awardEventId, questionId);
                CachedResults[(awardEventId, questionId)] = results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading results for award {awardEventId}, question {questionId}: {ex.Message}");
                CachedResults[(awardEventId, questionId)] = new List<QuestionResult>();
            }
        }

        // Method to create chronological timeline with phases and award events
        public List<object> GetChronologicalTimeline()
        {
            var timeline = new List<object>();
            
            if (Phases == null || AwardSettings == null) return timeline;

            // Add all phases to timeline (including past ones)
            foreach (var phase in Phases)
            {
                timeline.Add(new { Type = "Phase", Date = phase.StartDate, Item = phase });
                
                // Check if this phase should have an award event after it
                if (phase.Number % AwardSettings.PhasesBeforeAward == 0)
                {
                    var awardDate = phase.EndDate.AddDays(1);
                    
                    // Look for an existing award event for this time period
                    var awardEvent = AllAwardEvents.FirstOrDefault(ae => 
                        ae.StartDate >= awardDate && ae.StartDate <= awardDate.AddMonths(1));
                    
                    if (awardEvent != null)
                    {
                        timeline.Add(new { Type = "Award", Date = awardEvent.StartDate, Item = awardEvent });
                    }
                    else if (awardDate > DateProvider.Now)
                    {
                        // Only create placeholder for future award events
                        timeline.Add(new { Type = "FutureAward", Date = awardDate, Item = new { PhaseNumber = phase.Number, AwardDate = awardDate } });
                    }
                }
            }
            
            // Also add any standalone award events that might not have been matched to phases
            foreach (var awardEvent in AllAwardEvents)
            {
                if (!timeline.Any(t => ((dynamic)t).Type == "Award" && ((AwardEvent)((dynamic)t).Item).Id == awardEvent.Id))
                {
                    timeline.Add(new { Type = "Award", Date = awardEvent.StartDate, Item = awardEvent });
                }
            }
            
            // Sort by date (oldest first for chronological display)
            return timeline.OrderBy(t => ((dynamic)t).Date).ToList();
        }

        // Dictionary to track which award results are being shown
        private Dictionary<string, bool> showResultsDict = new Dictionary<string, bool>();

        public bool IsShowingResults(Guid awardEventId, Guid questionId) => 
            showResultsDict.ContainsKey($"show_{awardEventId}_{questionId}") && showResultsDict[$"show_{awardEventId}_{questionId}"];

        public void ToggleResults(Guid awardEventId, Guid questionId)
        {
            var key = $"show_{awardEventId}_{questionId}";
            if (!showResultsDict.ContainsKey(key))
                showResultsDict[key] = false;
            
            showResultsDict[key] = !showResultsDict[key];
            StateHasChanged();
        }
    }
}