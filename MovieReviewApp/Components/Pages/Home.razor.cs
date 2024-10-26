


using Microsoft.AspNetCore.Components;
using MovieReviewApp.Database;
using MovieReviewApp.Extensions;
using MovieReviewApp.Models;

namespace MovieReviewApp.Components.Pages
{
    public partial class Home
    {
        [Inject]
        private MongoDb db { get; set; } = default!;

        public MovieEvent? CurrentEvent;
        public MovieEvent? NextEvent;
        public List<Phase> Phases { get; set; } = new();
        private readonly Random _rand = new Random(1337);
        public bool RespectOrder = false;

        protected override void OnInitialized()
        {
            var settings = db.GetSettings();
            if (!DateTime.TryParse(settings.FirstOrDefault(x => x.Key == "StartDate")?.Value, out var startDate))
            {
                // Handle error: settings are missing or malformed
                return;
            }
            var setting = settings.FirstOrDefault(x => x.Key == "RespectOrder");
            if (setting != null && !string.IsNullOrEmpty(setting.Value))
                bool.TryParse(setting.Value, out RespectOrder);

            var allNames = db.GetAllPeople(RespectOrder).Select(x => x.Name).Where(x => !string.IsNullOrEmpty(x)).ToArray();
            if(allNames.Length == 0)
            {
                CurrentEvent = null;
                NextEvent = null;
                return;
            }
            GeneratePhases(startDate, allNames);
        }

        private void GeneratePhases(DateTime startDate, string?[]? allNames)
        {
            var listNames = allNames?.ToList();
            var phase = GeneratePhase(1, startDate, listNames); // Start with Phase 1
            Phases.Add(phase);

            // Keep generating phases until we reach a future date
            var isNextPhase = false;
            while (!isNextPhase)
            {
                startDate = phase.EndDate.AddDays(1);
                phase = GeneratePhase(phase.Number.Value + 1, startDate, listNames);
                Phases.Add(phase);
                isNextPhase = phase.StartDate > DateTime.Now; // Stop when we reach a future phase
            }
        }

        private Phase GeneratePhase(int phaseNumber, DateTime startDate, List<string> peopleNames)
        {
            // Get existing phase data from database
            var phase = db.GetPhase(phaseNumber, peopleNames, startDate);

            // Find who's already been assigned in this phase
            var peopleUsed = phase.Events.Where(x => !string.IsNullOrEmpty(x.Person))
                                        .Select(x => x.Person)
                                        .Distinct()
                                        .ToList();

            // If respecting order, cycle random seed to maintain consistent ordering
            if (!RespectOrder)
            {
                for (var i = 0; i < peopleUsed.Count; i++)
                    _rand.Next();
            }

            // Get remaining people who haven't been assigned yet
            var peopleLeftNotInDb = peopleNames.Where(x => !peopleUsed.Contains(x)).ToList();

            // Generate events for remaining people
            GenerateMovieEvents(phaseNumber, startDate, phase, peopleUsed, peopleLeftNotInDb);
            SetCurrentAndNextPhases(phase);

            return phase;
        }

        private void GenerateMovieEvents(int phaseNumber, DateTime startDate, Phase phase,
            List<string?> peopleUsed, List<string> peopleLeftNotInDb)
        {
            var moveEventStartDate = startDate.AddMonths(peopleUsed.Count);

            while (peopleLeftNotInDb.Count > 0)
            {
                // Pick next person - either in order or randomly
                var person = RespectOrder
                    ? peopleLeftNotInDb.First()
                    : peopleLeftNotInDb[_rand.Next(peopleLeftNotInDb.Count)];

                // Create event for this person
                phase.Events.Add(new MovieEvent
                {
                    StartDate = moveEventStartDate,
                    EndDate = moveEventStartDate.EndOfMonth(),
                    Person = person,
                    FromDatabase = false,
                    IsEditing = false,
                    PhaseNumber = phaseNumber
                });

                moveEventStartDate = moveEventStartDate.AddMonths(1);
                peopleLeftNotInDb.Remove(person);
            }
        }

        private void SetCurrentAndNextPhases(Phase phase)
        {
            var isCurrentPhase = DateTime.Now.IsWithinRange(phase.StartDate, phase.EndDate.EndOfDay());

            // Edge case: If we're before the first phase starts
            if (phase.Number == 1 && DateTime.Now < phase.StartDate)
            {
                CurrentEvent = null;
                NextEvent = phase.Events.OrderBy(x => x.StartDate).First();
                return;
            }

            if (isCurrentPhase)
            {
                CurrentEvent = phase.Events.Single(x => DateTime.Now.IsWithinRange(x.StartDate, x.EndDate.EndOfDay()));
                var nextMonth = DateTime.Now.AddMonths(1);
                NextEvent = phase.Events.FirstOrDefault(x => nextMonth.IsWithinRange(x.StartDate, x.EndDate.EndOfDay()));
                return;
            }

            if (phase.Events.Count == 0)
            {
                NextEvent = null;
                return;
            }

            //Assume it must be first one of the next phase.
            NextEvent = NextEvent == null ? phase.Events.OrderBy(x => x.StartDate).First() : NextEvent;
        }
    }
}
