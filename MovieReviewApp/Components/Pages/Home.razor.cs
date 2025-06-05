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
        public List<Phase> Phases { get; set; } = new();
        private readonly Random _rand = new(1337);

        // Cached properties
        private List<Setting> _settings;
        private List<Setting> Settings => _settings ??= movieReviewService.GetSettings();

        private DateTime? _startDate;
        private DateTime StartDate
        {
            get
            {
                if (!_startDate.HasValue)
                {
                    DateTime.TryParse(Settings.FirstOrDefault(x => x.Key == "StartDate")?.Value, out var date);
                    _startDate = date;
                }
                return _startDate.Value;
            }
        }

        private bool? _respectOrder;
        public bool RespectOrder
        {
            get
            {
                if (!_respectOrder.HasValue)
                {
                    var setting = Settings.FirstOrDefault(x => x.Key == "RespectOrder");
                    _respectOrder = setting != null && !string.IsNullOrEmpty(setting.Value) &&
                                  bool.TryParse(setting.Value, out var respect) && respect;
                }
                return _respectOrder.Value;
            }
        }

        private string[] _allNames;
        private string[] AllNames => _allNames ??= movieReviewService.GetAllPeople(RespectOrder)
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrEmpty(x))
            .ToArray();

        private AwardSetting? _awardSettings;
        private AwardSetting AwardSettings
        {
            get
            {
                if (_awardSettings == null)
                {
                    _awardSettings = movieReviewService.GetAwardSettings();
                }
                return _awardSettings;
            }
        }

        private List<MovieEvent> _existingEvents;
        private List<MovieEvent> ExistingEvents => _existingEvents ??= movieReviewService.GetAllMovieEvents().ToList();

        private bool? _isCurrentPhaseAwardPhase;
        public bool IsCurrentPhaseAwardPhase
        {
            get
            {
                if (!_isCurrentPhaseAwardPhase.HasValue)
                {
                    var currentAwardEvent = movieReviewService.GetAwardEventForDate(DateProvider.Now);
                    _isCurrentPhaseAwardPhase = currentAwardEvent != null;
                }
                return _isCurrentPhaseAwardPhase.Value;
            }
        }

        // Add new cached property for phases
        private List<Phase> _dbPhases;
        private List<Phase> DbPhases => _dbPhases ??= movieReviewService.GetAllPhases().OrderBy(p => p.StartDate).ToList();

        // Add cached property for discussion questions
        private List<DiscussionQuestion> _discussionQuestions;
        public List<DiscussionQuestion> DiscussionQuestions => _discussionQuestions ??= GetDiscussionQuestionsAsync().Result;

        protected override void OnInitialized()
        {
            if (AllNames.Length == 0 || StartDate == DateTime.MinValue)
            {
                CurrentEvent = null;
                NextEvent = null;
                return;
            }

            // Advance random number generator based on all existing events
            for (int i = 0; i < ExistingEvents.Count; i++)
            {
                _rand.Next(AllNames.Length);
            }

            GenerateSchedule(StartDate, AllNames);
        }

        private void GenerateSchedule(DateTime startDate, string[] allNames)
        {
            foreach (var phase in DbPhases)
            {
                // Generate events for this phase
                var generatedPhase = GeneratePhase(phase.Number, phase.StartDate, allNames.ToList());
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
                        var nextPhase = DbPhases.FirstOrDefault(p => p.Number == phase.Number + 1);
                        if (nextPhase != null)
                        {
                            var nextGeneratedPhase = GeneratePhase(nextPhase.Number, nextPhase.StartDate, allNames.ToList());
                            NextEvent = nextGeneratedPhase.Events.FirstOrDefault();
                        }
                        else
                        {
                            // Create a new phase if it doesn't exist yet
                            var newPhaseNumber = phase.Number + 1;
                            var newPhaseStartDate = awardMonthEnd.AddDays(1);
                            var newGeneratedPhase = GeneratePhase(newPhaseNumber, newPhaseStartDate, allNames.ToList());
                            Phases.Add(newGeneratedPhase);
                            NextEvent = newGeneratedPhase.Events.FirstOrDefault();
                        }
                    }
                }
            }

            // Generate additional future phases for display purposes without saving to DB
            // This ensures the timeline always shows future phases even if they're not yet in the database
            if (Phases.Any())
            {
                var lastPhase = Phases.OrderByDescending(p => p.Number).First();
                var additionalPhasesToGenerate = 2; // Generate next 2 phases beyond what's in the database

                for (int i = 1; i <= additionalPhasesToGenerate; i++)
                {
                    var newPhaseNumber = lastPhase.Number + i;
                    DateTime newPhaseStart;

                    // If the last phase ends with an award month
                    if (lastPhase.Number % AwardSettings.PhasesBeforeAward == 0)
                    {
                        var awardMonthEnd = lastPhase.EndDate.AddDays(1).AddMonths(1).AddDays(-1);
                        newPhaseStart = awardMonthEnd.AddDays(1);
                    }
                    else
                    {
                        newPhaseStart = lastPhase.EndDate.AddDays(1);
                    }

                    var futurePhase = GeneratePhase(newPhaseNumber, newPhaseStart, allNames.ToList());
                    Phases.Add(futurePhase);
                }
            }
        }

        private Phase GeneratePhase(int phaseNumber, DateTime startDate, List<string> peopleNames)
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
            var existingPhaseEvents = ExistingEvents.Where(e => e.PhaseNumber == phaseNumber);
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
                var personIndex = RespectOrder ? 0 : _rand.Next(availablePeople.Count);
                var person = availablePeople[personIndex];

                phase.Events.Add(new MovieEvent
                {
                    StartDate = currentDate,
                    EndDate = currentDate.EndOfMonth(),
                    Person = person,
                    PhaseNumber = phaseNumber,
                    FromDatabase = false,
                    IsEditing = false,
                    MeetupTime = currentDate.StartOfMonth().LastFridayOfMonth().AddHours(18) // Default to 6pm on last Friday of month
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
            if (firstRender)
            {
                var lastVisitStr = await JS.InvokeAsync<string>("localStorage.getItem", "lastVisit");
                var lastVisit = string.IsNullOrEmpty(lastVisitStr)
                    ? DateTime.UtcNow.AddDays(-1)
                    : DateTime.Parse(lastVisitStr);

                RecentUpdates = movieReviewService.GetRecentUpdates(lastVisit);
                await JS.InvokeVoidAsync("localStorage.setItem", "lastVisit", DateTime.UtcNow.ToString("o"));

                if (RecentUpdates.Any())
                {
                    StateHasChanged();
                }
            }
        }

        private async Task DismissUpdates()
        {
            showUpdates = false;
            await JS.InvokeVoidAsync("localStorage.setItem", "lastVisit", DateTime.UtcNow.ToString("o"));
        }

        private List<string> GetEligibleMoviesForPhase(int phaseNumber)
        {
            return ExistingEvents
                .Where(m => m.PhaseNumber <= phaseNumber &&
                           m.PhaseNumber > phaseNumber - AwardSettings.PhasesBeforeAward &&
                           !string.IsNullOrEmpty(m.Movie))
                .Select(m => m.Movie)
                .ToList();
        }

        private AwardEvent GetPreviousAwardEvent()
        {
            // If we're currently in a phase after an award month, get the previous award event
            if (DateProvider.Now < StartDate || DbPhases.Count == 0)
            {
                Console.WriteLine("No phases or before start date");
                return null;
            }

            // Find the current phase we're in
            var currentPhase = DbPhases.FirstOrDefault(p =>
                DateProvider.Now.IsWithinRange(p.StartDate, p.EndDate));

            if (currentPhase == null)
            {
                Console.WriteLine("Current phase not found");
                return null;
            }

            Console.WriteLine($"Current phase: {currentPhase.Number}, Start: {currentPhase.StartDate:yyyy-MM-dd}, End: {currentPhase.EndDate:yyyy-MM-dd}");

            // If the previous phase was an award phase
            var previousPhaseNumber = currentPhase.Number - 1;
            Console.WriteLine($"Previous phase number: {previousPhaseNumber}");

            if (previousPhaseNumber > 0 && previousPhaseNumber % AwardSettings.PhasesBeforeAward == 0)
            {
                Console.WriteLine("Previous phase was an award phase");

                // Calculate when the award month would have been
                var previousPhase = DbPhases.FirstOrDefault(p => p.Number == previousPhaseNumber);
                if (previousPhase == null)
                {
                    Console.WriteLine("Previous phase not found in database");
                    return null;
                }

                var awardMonthStart = previousPhase.EndDate.AddDays(1);
                var awardMonthEnd = awardMonthStart.AddMonths(1).AddDays(-1);

                Console.WriteLine($"Award month period: {awardMonthStart:yyyy-MM-dd} to {awardMonthEnd:yyyy-MM-dd}");

                // Get award event for that month
                var filter = Builders<AwardEvent>.Filter.And(
                    Builders<AwardEvent>.Filter.Gte(e => e.StartDate, awardMonthStart),
                    Builders<AwardEvent>.Filter.Lte(e => e.EndDate, awardMonthEnd)
                );

                var previousAwardEvent = movieReviewService.GetAwardEventByFilter(filter);
                if (previousAwardEvent == null)
                {
                    Console.WriteLine("No award event found for previous phase");
                }
                else
                {
                    Console.WriteLine($"Found previous award event: {previousAwardEvent.Id}, {previousAwardEvent.StartDate:yyyy-MM-dd}");
                }

                return previousAwardEvent;
            }

            Console.WriteLine("Previous phase was not an award phase");
            return null;
        }

        private async Task<List<DiscussionQuestion>> GetDiscussionQuestionsAsync()
        {
            return await discussionQuestionsService.GetActiveQuestionsAsync();
        }
    }
}