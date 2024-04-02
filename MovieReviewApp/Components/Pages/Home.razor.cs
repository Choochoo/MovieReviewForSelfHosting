using MongoDB.Driver.Linq;
using MovieReviewApp.Database;
using MovieReviewApp.Models;
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
            var startDate = DateTime.Parse(settings.First(x => x.Key == "StartDate").Value);
            TimeCount = int.Parse(settings.First(x => x.Key == "TimeCount").Value);
            TimePeriod = settings.First(x => x.Key == "TimePeriod").Value;
            var allNames = db.GetAllPeople().Select(x => x.Name).ToArray();

            DateTime endOfCurrentPeriod = startDate;
            var today = DateTime.Now;
            List<string> listNames = allNames.Where(x => !string.IsNullOrEmpty(x)).ToList();
            string person = "";
            while (endOfCurrentPeriod.Date < today.Date)
            {
                endOfCurrentPeriod = AddToDateTimeWithCountAndPeriod(endOfCurrentPeriod, TimeCount.Value, TimePeriod);
                if (listNames.Count == 0)
                    listNames = allNames.ToList();
                person = listNames[rand.Next(listNames.Count)];
                listNames = listNames.Where(x => x != person).ToList();
            }

            var dbCurrentEvent = db.GetMovieEventBetweenDate(endOfCurrentPeriod.AddDays(-1));
            if (dbCurrentEvent != null)
            {
                listNames = listNames.Where(x => x != dbCurrentEvent.Person).ToList();
                CurrentEvent = dbCurrentEvent;
                CurrentEvent.FromDatabase = true;
                var nextEventDate = AddToDateTimeWithCountAndPeriod(endOfCurrentPeriod, TimeCount.Value, TimePeriod);
                var dbNextEvent = db.GetMovieEventBetweenDate(nextEventDate);
                if (dbNextEvent != null)
                {
                    NextEvent = dbNextEvent;
                    NextEvent.FromDatabase = true;
                    return;
                }
            }


            if (listNames.Count == 0)
                listNames = allNames.ToList();
            if (person == "")
                person = listNames.ElementAt(rand.Next(listNames.Count));
            if (listNames.Count == 0)
                listNames = allNames.ToList();
            string nextPerson = listNames.ElementAt(rand.Next(listNames.Count));
            listNames = listNames.Where(x => x != nextPerson).ToList();
            DateTime startOfPeriod, endOfPeriod, endOfNextPeriod;
            if(endOfCurrentPeriod.Date == startDate.Date)
            {
                startOfPeriod = startDate;
                endOfPeriod = AddToDateTimeWithCountAndPeriod(endOfCurrentPeriod, TimeCount.Value, TimePeriod);
                endOfNextPeriod = AddToDateTimeWithCountAndPeriod(endOfCurrentPeriod, TimeCount.Value*2, TimePeriod);
            }
            else
            {
                startOfPeriod = AddToDateTimeWithCountAndPeriod(endOfCurrentPeriod, -TimeCount.Value, TimePeriod);
                endOfPeriod = endOfCurrentPeriod;
                endOfNextPeriod = AddToDateTimeWithCountAndPeriod(endOfCurrentPeriod, TimeCount.Value, TimePeriod);
            }


            if (CurrentEvent == null)
            {
                CurrentEvent = new MovieEvent
                {
                    StartDate = startOfPeriod,
                    EndDate = endOfPeriod.AddDays(-1),
                    Person = person,
                    FromDatabase = false,
                    IsEditing = true
                };
            }

            NextEvent = new MovieEvent
            {
                StartDate = endOfPeriod,
                EndDate = endOfNextPeriod.AddDays(-1),
                Person = nextPerson,
                FromDatabase = false,
                IsEditing = true
            };


            while (listNames.Any())
            {
                string sdate = endOfNextPeriod.ToString("MMMM d, yyyy");
                person = listNames[rand.Next(listNames.Count)];
                listNames = listNames.Where(x => x != person).ToList();
                endOfNextPeriod = AddToDateTimeWithCountAndPeriod(endOfNextPeriod, TimeCount.Value, TimePeriod);
                Remaining.Add((person, $"{sdate} - {endOfNextPeriod.AddDays(-1).ToString("MMMM d, yyyy")}"));
            }
        }

        private DateTime AddToDateTimeWithCountAndPeriod(DateTime date, int count, string period)
        {
            if (period == "Month")
                return date.AddMonths(count);
            if (period == "Week")
                return date.AddDays(count * 7);
            if (period == "Day")
                return date.AddDays(count);
            return date;
        }
    }
}