using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MovieReviewApp.Database;
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
        private MongoDb db { get; set; } = default!;

        private List<SiteUpdate> RecentUpdates { get; set; } = new();
        private bool showUpdates = true;
        public MovieEvent? CurrentEvent;
        public MovieEvent? NextEvent;
        public List<Phase> Phases { get; set; } = new();
        private readonly Random _rand = new(1337);

        // Cached properties
        private List<Setting> _settings;
        private List<Setting> Settings => _settings ??= db.GetSettings();

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
        private string[] AllNames => _allNames ??= db.GetAllPeople(RespectOrder)
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
                    _awardSettings = db.GetAwardSettings();
                }
                return _awardSettings;
            }
        }

        private List<MovieEvent> _existingEvents;
        private List<MovieEvent> ExistingEvents => _existingEvents ??= db.GetAllMovieEvents().ToList();

        private bool? _isCurrentPhaseAwardPhase;
        public bool IsCurrentPhaseAwardPhase
        {
            get
            {
                if (!_isCurrentPhaseAwardPhase.HasValue)
                {
                    var currentAwardEvent = db.GetAwardEventForDate(DateProvider.Now);
                    _isCurrentPhaseAwardPhase = currentAwardEvent != null;
                }
                return _isCurrentPhaseAwardPhase.Value;
            }
        }

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
            var currentDate = startDate;
            var phaseNumber = 1;

            while (currentDate <= DateProvider.Now.AddYears(1))
            {
                var phase = GeneratePhase(phaseNumber, currentDate, allNames.ToList());
                Phases.Add(phase);

                if (DateProvider.Now.IsWithinRange(phase.StartDate, phase.EndDate))
                {
                    UpdateCurrentAndNextEvents(phase);
                }

                if (AwardSettings.AwardsEnabled && phaseNumber % AwardSettings.PhasesBeforeAward == 0)
                {
                    var awardDate = phase.EndDate.AddDays(1);
                    var awardMonthEnd = awardDate.AddMonths(1).AddDays(-1);

                    // Only get or create award event if we're within a month of its start
                    if (DateProvider.Now >= awardDate.AddMonths(-1) && DateProvider.Now <= awardMonthEnd)
                    {
                        var awardEvent = db.GetAwardEventForDate(awardDate);

                        if (DateProvider.Now.IsWithinRange(awardDate, awardMonthEnd))
                        {
                            CurrentEvent = null;
                        }
                    }

                    currentDate = awardDate.AddMonths(1);
                }
                else
                {
                    currentDate = phase.EndDate.AddDays(1);
                }
                phaseNumber++;
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
                    IsEditing = false
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

                RecentUpdates = db.GetRecentUpdates(lastVisit);
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
    }
}