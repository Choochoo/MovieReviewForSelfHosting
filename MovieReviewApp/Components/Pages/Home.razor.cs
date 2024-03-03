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


            var listNames = allNames.ToList();
            string person = "";
            while (endOfCurrentPeriod.Date < today.Date)
            {
                endOfCurrentPeriod = AddToDateTimeWithCountAndPeriod(endOfCurrentPeriod, TimeCount.Value, TimePeriod);
                if (listNames.Count == 0)
                    listNames = allNames.ToList();
                person = listNames.ElementAt(rand.Next(listNames.Count));
            }

            var dbCurrentEvent = db.GetMovieEventBetweenDate(endOfCurrentPeriod.AddDays(-2));
            if (dbCurrentEvent != null)
            {
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
            CurrentEvent = new MovieEvent
            {
                StartDate = startOfPeriod,
                EndDate = endOfPeriod.AddDays(-1),
                Person = person,
                FromDatabase = false,
                IsEditing = true
            };

            NextEvent = new MovieEvent
            {
                StartDate = endOfPeriod,
                EndDate = endOfNextPeriod,
                Person = nextPerson,
                FromDatabase = false,
                IsEditing = true
            };
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