using MongoDB.Driver.Linq;
using MovieReviewApp.Database;
using MovieReviewApp.Models;

namespace MovieReviewApp.Components.Pages
{
    public partial class Home
    {
        public MovieEvent? CurrentEvent;
        public MovieEvent? NextEvent;

        private Random rand = new Random(1337);
        private MongoDb db = new MongoDb();

        private String[] AllNames = new[] { "Jeremiah", "Lacey", "Jared", "Dave", "Keri" };
        protected override void OnInitialized()
        {
            DateTime EndOfCurrentPeriod = new DateTime(2024, 1, 1);
            var today = DateTime.Now;
            var dbCurrentEvent = db.GetMovieEventBetweenDate(today);
            if(dbCurrentEvent != null)
            {
                CurrentEvent = dbCurrentEvent;
                CurrentEvent.FromDatabase = true;
                var dbNextEvent = db.GetMovieEventBetweenDate(today.AddDays(14));
                if(dbNextEvent != null)
                {
                    NextEvent = dbNextEvent;
                    NextEvent.FromDatabase = true;

                    return;
                }
            }

            var listNames = AllNames.ToList();
            string person = "";
            while (EndOfCurrentPeriod.Date < today.Date)
            {
                EndOfCurrentPeriod = EndOfCurrentPeriod.AddDays(14);
                if (listNames.Count == 0)
                    listNames = AllNames.ToList();
                person = listNames.ElementAt(rand.Next(listNames.Count));
            }
            if (listNames.Count == 0)
                listNames = AllNames.ToList();
            string nextPerson = listNames.ElementAt(rand.Next(listNames.Count));

            var StartOfPeriod = EndOfCurrentPeriod.AddDays(-14);
            var EndOfPeriod = EndOfCurrentPeriod;
            var EndOfNextPeriod = EndOfPeriod.AddDays(14);

            if(CurrentEvent == null)
            CurrentEvent = new MovieEvent
            {
                StartDate = StartOfPeriod,
                EndDate = EndOfPeriod.AddDays(-1),
                Person = person,
                FromDatabase = false,
                IsEditing = true
            };

            NextEvent = new MovieEvent
            {
                StartDate = EndOfPeriod,
                EndDate = EndOfNextPeriod,
                Person = nextPerson,
                FromDatabase = false,
                IsEditing = true
            };
        }
    }
}