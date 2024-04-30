using MongoDB.Driver.Linq;
using MovieReviewApp.Database;
using MovieReviewApp.Models;
using System;
using System.Threading;

namespace MovieReviewApp.Components.Pages
{
    public partial class Home
    {
        public MovieEvent? CurrentEvent;
        public MovieEvent? NextEvent;
        public int? TimeCount = null;
        public string? TimePeriod = null;
        public List<(string, string)> Remaining { get; set; } = new();

        private Random rand = new Random(1337);
        private MongoDb db = new MongoDb();

        protected override void OnInitialized()
        {
            var settings = db.GetSettings();
            if (!DateTime.TryParse(settings.FirstOrDefault(x => x.Key == "StartDate")?.Value, out var startDate) ||
                !int.TryParse(settings.FirstOrDefault(x => x.Key == "TimeCount")?.Value, out var timeCount) ||
                (TimePeriod = settings.FirstOrDefault(x => x.Key == "TimePeriod")?.Value) == null)
            {
                // Handle error: settings are missing or malformed
                return;
            }

            TimeCount = timeCount;
            var allNames = db.GetAllPeople().Select(x => x.Name).Where(x => !string.IsNullOrEmpty(x)).ToArray();

            var today = DateTime.Now;
            List<string> listNames = [.. allNames];
            var endOfCurrentPeriod = startDate;
            string person = "";

            while (endOfCurrentPeriod.Date < today.Date)
            {
                endOfCurrentPeriod = AddToDateTimeWithCountAndPeriod(endOfCurrentPeriod, TimeCount.Value, TimePeriod);
                if (listNames.Count == 0)
                    listNames = [.. allNames];
                person = listNames[rand.Next(listNames.Count)];
                listNames.Remove(person);
            }
            var dbCurrentEvent = db.GetMovieEventBetweenDate(endOfCurrentPeriod);
            if (dbCurrentEvent != null)
            {
                listNames.Remove(dbCurrentEvent.Person);
                CurrentEvent = dbCurrentEvent;
                CurrentEvent.FromDatabase = true;

                var nextEventDate = AddToDateTimeWithCountAndPeriod(endOfCurrentPeriod, TimeCount.Value, TimePeriod);
                var dbNextEvent = db.GetMovieEventBetweenDate(nextEventDate);
                if (dbNextEvent != null)
                {
                    NextEvent = dbNextEvent;
                    NextEvent.FromDatabase = true;
                    listNames.Remove(dbNextEvent.Person);
                    FinishRemainingNames(listNames, nextEventDate);
                    return;
                }
            }

            person = listNames[rand.Next(listNames.Count)];
            listNames.Remove(person);

            var (startOfPeriod, endOfPeriod, endOfNextPeriod) = CalculatePeriods(endOfCurrentPeriod, startDate);

            if (CurrentEvent == null)
            {
                CurrentEvent = new MovieEvent
                {
                    StartDate = startOfPeriod,
                    EndDate = endOfPeriod,
                    Person = person,
                    FromDatabase = false,
                    IsEditing = true
                };
                person = listNames[rand.Next(listNames.Count)];
                listNames.Remove(person);
            }

            NextEvent = new MovieEvent
            {
                StartDate = endOfPeriod.AddDays(1),
                EndDate = endOfNextPeriod,
                Person = person,
                FromDatabase = false,
                IsEditing = true
            };

            FinishRemainingNames(listNames,endOfNextPeriod);
        }

        private void FinishRemainingNames(List<string> listNames, DateTime endOfNextPeriod)
        {
            string person = string.Empty;
            while (listNames.Any())
            {
                var sdate = endOfNextPeriod.AddDays(1).ToString("MMMM d, yyyy");
                person = listNames[rand.Next(listNames.Count)];
                listNames.Remove(person);
                endOfNextPeriod = AddToDateTimeWithCountAndPeriod(endOfNextPeriod, TimeCount.Value, TimePeriod);
                Remaining.Add((person, $"{sdate} - {endOfNextPeriod.ToString("MMMM d, yyyy")}"));
            }
        }

        private DateTime AddToDateTimeWithCountAndPeriod(DateTime date, int count, string period)
        {
            switch (period)
            {
                case "Month":
                    date = date.AddMonths(count);
                    return new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)); ;
                case "Week":
                    return date.AddDays(count * 7);
                default:
                    return date.AddDays(count);
            }
        }

        private (DateTime, DateTime, DateTime) CalculatePeriods(DateTime endOfCurrentPeriod, DateTime startDate)
        {
            DateTime startOfPeriod, endOfPeriod, endOfNextPeriod;
            if (endOfCurrentPeriod.Date == startDate.Date)
            {
                startOfPeriod = startDate;
                endOfPeriod = AddToDateTimeWithCountAndPeriod(endOfCurrentPeriod, TimeCount.Value, TimePeriod);
                endOfNextPeriod = AddToDateTimeWithCountAndPeriod(endOfCurrentPeriod, TimeCount.Value * 2, TimePeriod);
            }
            else
            {
                startOfPeriod = AddToDateTimeWithCountAndPeriod(endOfCurrentPeriod, -TimeCount.Value, TimePeriod).AddDays(1);
                endOfPeriod = endOfCurrentPeriod;
                endOfNextPeriod = AddToDateTimeWithCountAndPeriod(endOfCurrentPeriod, TimeCount.Value, TimePeriod);
            }
            return (startOfPeriod, endOfPeriod, endOfNextPeriod);
        }
    }
}