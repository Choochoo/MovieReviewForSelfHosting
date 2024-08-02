

using MovieReviewApp.Database;
using MovieReviewApp.Extensions;
using MovieReviewApp.Models;

namespace MovieReviewApp.Components.Pages
{
    public partial class Home
    {
        public MovieEvent? CurrentEvent;
        public MovieEvent? NextEvent;
        public List<Phase> Phases { get; set; } = new();
        private readonly Random _rand = new Random(1337);
        private MongoDb db = new MongoDb();

        protected override void OnInitialized()
        {
            var settings = db.GetSettings();
            if (!DateTime.TryParse(settings.FirstOrDefault(x => x.Key == "StartDate")?.Value, out var startDate))
            {
                // Handle error: settings are missing or malformed
                return;
            }

            var allNames = db.GetAllPeople().Select(x => x.Name).Where(x => !string.IsNullOrEmpty(x)).ToArray();
            GeneratePhases(startDate, allNames);
        }

        private void GeneratePhases(DateTime startDate, string?[]? allNames)
        {
            var listNames = allNames?.ToList();
            var phase = GeneratePhase(1, startDate, listNames); // Always phase 1
            Phases.Add(phase);
            var isNextPhase = false;
            while (!isNextPhase)
            {
                startDate = phase.EndDate.AddDays(1);
                phase = GeneratePhase(phase.Number.Value + 1, startDate, listNames);
                Phases.Add(phase);
                isNextPhase = phase.StartDate > DateTime.Now;
            }
        }

        private Phase GeneratePhase(int phaseNumber, DateTime startDate, List<string> peopleNames)
        {
            var phase = db.GetPhase(phaseNumber, peopleNames, startDate);
            var peopleUsed = phase.Events.Where(x => !string.IsNullOrEmpty(x.Person)).Select(x => x.Person).Distinct().ToList();
            for (var i = 0; i < peopleUsed.Count; i++)
                _rand.Next();//cycle to where we are in the seed.

            var peopleLeftNotInDb = peopleNames.Where(x => !peopleUsed.Contains(x)).ToList();
            GenerateMovieEvents(phaseNumber, startDate, phase, peopleUsed, peopleLeftNotInDb);
            SetCurrentAndNextPhases(phase);

            return phase;
        }

        private void GenerateMovieEvents(int phaseNumber, DateTime startDate, Phase phase, List<string?> peopleUsed, List<string> peopleLeftNotInDb)
        {
            var moveEventStartDate = startDate.AddMonths(peopleUsed.Count);
            while (peopleLeftNotInDb.Count > 0)
            {
                var person = peopleLeftNotInDb[_rand.Next(peopleLeftNotInDb.Count)];
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
            if (isCurrentPhase)
            {
                CurrentEvent = phase.Events.Single(x => DateTime.Now.IsWithinRange(x.StartDate, x.EndDate.EndOfDay()));
                var nextMonth = DateTime.Now.AddMonths(1);
                NextEvent = phase.Events.FirstOrDefault(x => nextMonth.IsWithinRange(x.StartDate, x.EndDate.EndOfDay()));
                return;
            }

            //Assume it must be first one of the next phase.
            NextEvent = NextEvent == null ? phase.Events.OrderBy(x => x.StartDate).First() : NextEvent;
        }
    }
}
