


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

        private bool IsCurrentPhaseAwardPhase =>
            db.GetAwardEventForDate(DateProvider.Now) != null;

        private List<SiteUpdate> RecentUpdates { get; set; } = new();
        private bool showUpdates = true;
        public MovieEvent? CurrentEvent;
        public MovieEvent? NextEvent;
        public List<Phase> Phases { get; set; } = new();
        private readonly Random _rand = new(1337);
        public bool RespectOrder = false;

        protected override void OnInitialized()
        {
            var settings = db.GetSettings();
            if (!DateTime.TryParse(settings.FirstOrDefault(x => x.Key == "StartDate")?.Value, out var startDate))
            {
                return;
            }

            var setting = settings.FirstOrDefault(x => x.Key == "RespectOrder");
            if (setting != null && !string.IsNullOrEmpty(setting.Value))
                bool.TryParse(setting.Value, out RespectOrder);

            var allNames = db.GetAllPeople(RespectOrder).Select(x => x.Name)
                .Where(x => !string.IsNullOrEmpty(x)).ToArray();

            if (allNames.Length == 0)
            {
                CurrentEvent = null;
                NextEvent = null;
                return;
            }

            // Advance random number generator based on all existing events
            var totalEvents = db.GetAllMovieEvents().Count();
            for (int i = 0; i < totalEvents; i++)
            {
                _rand.Next(allNames.Length);
            }

            GenerateSchedule(startDate, allNames);
        }

        private void GenerateSchedule(DateTime startDate, string[] allNames)
        {
            var currentDate = startDate;
            var phaseNumber = 1;
            var awardSettings = db.GetAwardSettings();

            while (currentDate <= DateProvider.Now.AddYears(1)) // Generate schedule for next year
            {
                // Generate regular phase
                var phase = GeneratePhase(phaseNumber, currentDate, allNames.ToList());
                Phases.Add(phase);

                // Update current and next events if we're in this phase
                if (DateProvider.Now.IsWithinRange(phase.StartDate, phase.EndDate))
                {
                    UpdateCurrentAndNextEvents(phase);
                }

                // Check if we need to add an awards period after this phase
                if (awardSettings.AwardsEnabled && phaseNumber % awardSettings.PhasesBeforeAward == 0)
                {
                    var awardDate = phase.EndDate.AddDays(1);
                    var awardEvent = db.GetAwardEventForDate(awardDate);

                    // Check if we're currently in an awards period
                    if (DateProvider.Now.IsWithinRange(awardDate, awardDate.AddMonths(1).AddDays(-1)))
                    {
                        // If we're in the award period, clear the current event
                        CurrentEvent = null;
                    }

                    // Move the current date past the award month
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

            // Get existing events from database
            var existingEvents = db.GetPhaseEvents(phaseNumber);
            foreach (var existingEvent in existingEvents)
            {
                phase.Events.Add(existingEvent);
                availablePeople.Remove(existingEvent.Person);
                currentDate = existingEvent.EndDate.AddDays(1);
            }

            // Generate remaining events
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
            var currentMonthEvents = phase.Events
                .Where(e => DateProvider.Now.IsWithinRange(e.StartDate, e.EndDate))
                .ToList();

            CurrentEvent = currentMonthEvents.FirstOrDefault();

            var nextMonthDate = DateProvider.Now.AddMonths(1);
            NextEvent = phase.Events
                .FirstOrDefault(e => nextMonthDate.IsWithinRange(e.StartDate, e.EndDate));
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                // Get last visit time from localStorage, default to 24 hours ago if not found
                var lastVisitStr = await JS.InvokeAsync<string>("localStorage.getItem", "lastVisit");
                var lastVisit = string.IsNullOrEmpty(lastVisitStr)
                    ? DateTime.UtcNow.AddDays(-1)
                    : DateTime.Parse(lastVisitStr);

                // Get updates since last visit
                RecentUpdates = db.GetRecentUpdates(lastVisit);

                // Update last visit time
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
            // Update last visit time when dismissed
            await JS.InvokeVoidAsync("localStorage.setItem", "lastVisit", DateTime.UtcNow.ToString("o"));
        }

    }
}
