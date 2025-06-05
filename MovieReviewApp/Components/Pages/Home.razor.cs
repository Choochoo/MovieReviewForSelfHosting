using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MongoDB.Driver;
using MovieReviewApp.Extensions;
using MovieReviewApp.Models;
using MovieReviewApp.Services;
using System.ComponentModel.DataAnnotations.Schema;

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

        // Cached properties
        private List<Setting> _settings;
        private async Task<List<Setting>> GetSettingsAsync()
        {
            if (_settings == null)
            {
                _settings = await movieReviewService.GetSettingsAsync();
            }
            return _settings;
        }

        private DateTime? _startDate;
        private async Task<DateTime> GetStartDateAsync()
        {
            if (!_startDate.HasValue)
            {
                var settings = await GetSettingsAsync();
                DateTime.TryParse(settings.FirstOrDefault(x => x.Key == "StartDate")?.Value, out var date);
                _startDate = date;
            }
            return _startDate.Value;
        }

        private bool? _respectOrder;
        private async Task<bool> GetRespectOrderAsync()
        {
            if (!_respectOrder.HasValue)
            {
                var settings = await GetSettingsAsync();
                var setting = settings.FirstOrDefault(x => x.Key == "RespectOrder");
                _respectOrder = setting != null && !string.IsNullOrEmpty(setting.Value) &&
                              bool.TryParse(setting.Value, out var respect) && respect;
            }
            return _respectOrder.Value;
        }

        private string[] _allNames;
        private async Task<string[]> GetAllNamesAsync()
        {
            if (_allNames == null)
            {
                var respectOrder = await GetRespectOrderAsync();
                var people = await movieReviewService.GetAllPeopleAsync(respectOrder);
                _allNames = people.Select(x => x.Name)
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToArray();
            }
            return _allNames;
        }

        private AwardSetting? _awardSettings;
        private async Task<AwardSetting> GetAwardSettingsAsync()
        {
            if (_awardSettings == null)
            {
                _awardSettings = await movieReviewService.GetAwardSettingsAsync();
            }
            return _awardSettings;
        }

        private List<MovieEvent> _existingEvents;
        private async Task<List<MovieEvent>> GetExistingEventsAsync()
        {
            if (_existingEvents == null)
            {
                _existingEvents = (await movieReviewService.GetAllMovieEventsAsync()).ToList();
            }
            return _existingEvents;
        }

        private bool? _isCurrentPhaseAwardPhase;
        private async Task<bool> GetIsCurrentPhaseAwardPhaseAsync()
        {
            if (!_isCurrentPhaseAwardPhase.HasValue)
            {
                var currentAwardEvent = await movieReviewService.GetAwardEventForDateAsync(DateProvider.Now);
                _isCurrentPhaseAwardPhase = currentAwardEvent != null;
            }
            return _isCurrentPhaseAwardPhase.Value;
        }

        // Add new cached property for phases
        private List<Phase> _dbPhases;
        private async Task<List<Phase>> GetDbPhasesAsync()
        {
            if (_dbPhases == null)
            {
                _dbPhases = (await movieReviewService.GetAllPhasesAsync()).OrderBy(p => p.StartDate).ToList();
            }
            return _dbPhases;
        }

        // Add cached property for discussion questions
        private List<DiscussionQuestion>? _discussionQuestions;
        public List<DiscussionQuestion>? DiscussionQuestions => _discussionQuestions;

        // Add cached property for award phase status
        public bool IsCurrentPhaseAwardPhase => GetIsCurrentPhaseAwardPhaseAsync().GetAwaiter().GetResult();

        // Add cached property for award settings
        public AwardSetting AwardSettings => GetAwardSettingsAsync().GetAwaiter().GetResult();

        protected override async Task OnInitializedAsync()
        {
            // Load discussion questions asynchronously
            _discussionQuestions = await discussionQuestionsService.GetActiveQuestionsAsync();
            
            var allNames = await GetAllNamesAsync();
            var startDate = await GetStartDateAsync();
            
            if (allNames.Length == 0 || startDate == DateTime.MinValue)
            {
                CurrentEvent = null;
                NextEvent = null;
                return;
            }

            // Advance random number generator based on all existing events
            var existingEvents = await GetExistingEventsAsync();
            for (int i = 0; i < existingEvents.Count; i++)
            {
                _rand.Next(allNames.Length);
            }

            await GenerateScheduleAsync(startDate, allNames);
        }

        private async Task GenerateScheduleAsync(DateTime startDate, string[] allNames)
        {
            var dbPhases = await GetDbPhasesAsync();
            var awardSettings = await GetAwardSettingsAsync();
            
            foreach (var phase in dbPhases)
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
                if (phase.Number % awardSettings.PhasesBeforeAward == 0)
                {
                    var awardDate = phase.EndDate.AddDays(1);
                    var awardMonthEnd = awardDate.AddMonths(1).AddDays(-1);

                    if (DateProvider.Now.IsWithinRange(awardDate, awardMonthEnd))
                    {
                        CurrentEvent = null;
                        var nextPhase = dbPhases.FirstOrDefault(p => p.Number == phase.Number + 1);
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
                    if (lastPhase.Number % awardSettings.PhasesBeforeAward == 0)
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
            var existingEvents = await GetExistingEventsAsync();
            var existingPhaseEvents = existingEvents.Where(e => e.PhaseNumber == phaseNumber);
            var respectOrder = await GetRespectOrderAsync();
            
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
                var personIndex = respectOrder ? 0 : _rand.Next(availablePeople.Count);
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

                RecentUpdates = await movieReviewService.GetRecentUpdatesAsync(lastVisit);
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

        private async Task<List<string>> GetEligibleMoviesForPhaseAsync(int phaseNumber)
        {
            var existingEvents = await GetExistingEventsAsync();
            var awardSettings = await GetAwardSettingsAsync();
            
            return existingEvents
                .Where(m => m.PhaseNumber <= phaseNumber &&
                           m.PhaseNumber > phaseNumber - awardSettings.PhasesBeforeAward &&
                           !string.IsNullOrEmpty(m.Movie))
                .Select(m => m.Movie)
                .ToList();
        }

        private async Task<AwardEvent> GetPreviousAwardEventAsync()
        {
            // If we're currently in a phase after an award month, get the previous award event
            var startDate = await GetStartDateAsync();
            var dbPhases = await GetDbPhasesAsync();
            var awardSettings = await GetAwardSettingsAsync();
            
            if (DateProvider.Now < startDate || dbPhases.Count == 0)
            {
                Console.WriteLine("No phases or before start date");
                return null;
            }

            // Find the current phase we're in
            var currentPhase = dbPhases.FirstOrDefault(p =>
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

            if (previousPhaseNumber > 0 && previousPhaseNumber % awardSettings.PhasesBeforeAward == 0)
            {
                Console.WriteLine("Previous phase was an award phase");

                // Calculate when the award month would have been
                var previousPhase = dbPhases.FirstOrDefault(p => p.Number == previousPhaseNumber);
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

                var previousAwardEvent = await movieReviewService.GetAwardEventByFilterAsync(filter);
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

        public List<string> GetEligibleMoviesForPhase(int phaseNumber)
        {
            return GetEligibleMoviesForPhaseAsync(phaseNumber).GetAwaiter().GetResult();
        }

        public AwardEvent? GetPreviousAwardEvent()
        {
            return GetPreviousAwardEventAsync().GetAwaiter().GetResult();
        }

    }
}