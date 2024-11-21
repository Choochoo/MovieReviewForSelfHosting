using MongoDB.Driver;
using MovieReviewApp.Extensions;
using MovieReviewApp.Models;

namespace MovieReviewApp.Database
{
    public class MongoDb
    {
        private readonly IMongoDatabase? database;

        public MongoDb(IConfiguration configuration)
        {
            string mongoEnvVariable = configuration.GetValue<string>("MongoEnvironmentVariable");
            string mongoUri = Environment.GetEnvironmentVariable(mongoEnvVariable);
            if (string.IsNullOrEmpty(mongoUri))
            {
                throw new ArgumentNullException("MongoDB connection string not found in environment variables");
            }
            IMongoClient client;
            client = new MongoClient(mongoUri);
            database = client.GetDatabase("MovieReview");
        }

        private IMongoCollection<Phase>? Phases => database?.GetCollection<Phase>("Phases");
        private IMongoCollection<MovieEvent>? MovieEvents => database?.GetCollection<MovieEvent>("MovieReviews");
        private IMongoCollection<Person>? People => database?.GetCollection<Person>("People");
        private IMongoCollection<Setting>? Settings => database?.GetCollection<Setting>("Settings");
        private IMongoCollection<StatsCommand>? StatsCommands => database?.GetCollection<StatsCommand>("StatsCommands");
        private IMongoCollection<SiteUpdate> SiteUpdates => database?.GetCollection<SiteUpdate>("SiteUpdates");

        public MovieEvent GetMovieEventBetweenDate(DateTime dt)
        {
            var filter = Builders<MovieEvent>.Filter.And(
                Builders<MovieEvent>.Filter.Lte("StartDate", dt),
                Builders<MovieEvent>.Filter.Gte("EndDate", dt)
            );
            return MovieEvents.Find<MovieEvent>(filter).FirstOrDefault();
        }

        public void AddSiteUpdate(string updateType, string description)
        {
            var update = new SiteUpdate
            {
                LastUpdateTime = DateTime.UtcNow,
                UpdateType = updateType,
                Description = description
            };
            SiteUpdates.InsertOne(update);
        }

        public DateTime? GetLatestUpdateTime()
        {
            var latestUpdate = SiteUpdates
                .Find(_ => true)
                .SortByDescending(x => x.LastUpdateTime)
                .FirstOrDefault();
            return latestUpdate?.LastUpdateTime;
        }

        public List<SiteUpdate> GetRecentUpdates(DateTime since)
        {
            return SiteUpdates
                .Find(x => x.LastUpdateTime < since)
                .SortByDescending(x => x.LastUpdateTime)
                .ToList();
        }

        public void AddOrUpdateMovieEvent(MovieEvent movieEvent)
        {
            var filter = Builders<MovieEvent>.Filter.Eq("Id", movieEvent.Id);
            var existingMovie = MovieEvents.Find(filter).FirstOrDefault();
            var isNew = existingMovie == null || string.IsNullOrEmpty(existingMovie.Movie);

            var update = Builders<MovieEvent>.Update
                .Set("StartDate", movieEvent.StartDate)
                .Set("EndDate", movieEvent.EndDate)
                .Set("Person", movieEvent.Person)
                .Set("Movie", movieEvent.Movie)
                .Set("DownloadLink", movieEvent.DownloadLink)
                .Set("PosterUrl", movieEvent.PosterUrl)
                .Set("IMDb", movieEvent.IMDb)
                .Set("Reasoning", movieEvent.Reasoning)
                .Set("AlreadySeen", movieEvent.AlreadySeen)
                .Set("SeenDate", movieEvent.SeenDate)
                .Set("MeetupTime", movieEvent.MeetupTime?.ToLocalTime())
                .Set("PhaseNumber", movieEvent.PhaseNumber)
                .Set("Synopsis", movieEvent.Synopsis);

            MovieEvents.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });


            if (string.IsNullOrEmpty(movieEvent.Movie)) return;

            CreateUpdateNotification(movieEvent, existingMovie, isNew);
        }

        private void CreateUpdateNotification(MovieEvent movieEvent, MovieEvent existingMovie, bool isNew)
        {
            if (isNew)
            {
                var newMovieChanges = new List<string>
        {
            $"• Movie: {movieEvent.Movie}"
        };

                if (!string.IsNullOrEmpty(movieEvent.IMDb))
                    newMovieChanges.Add("• IMDb Link Added");

                if (!string.IsNullOrEmpty(movieEvent.PosterUrl))
                    newMovieChanges.Add("• Poster Added");

                if (!string.IsNullOrEmpty(movieEvent.Reasoning))
                    newMovieChanges.Add("• Reasoning Added");

                if (movieEvent.MeetupTime.HasValue)
                    newMovieChanges.Add($"• Meetup Time: {movieEvent.MeetupTime.Value:MM/dd/yyyy h:mm tt}");

                newMovieChanges.Add(movieEvent.AlreadySeen
                    ? $"• Previously seen in {movieEvent.SeenDate?.Year}"
                    : "• Not previously seen");

                AddSiteUpdate("MovieAdded",
                    $"New movie added for {movieEvent.StartDate:MMMM yyyy} by {movieEvent.Person}:\n" +
                    string.Join("\n", newMovieChanges));

                return;
            }

            if (existingMovie == null) return;

            var changes = new List<string>();

            if (movieEvent.Movie != existingMovie.Movie)
                changes.Add($"• Movie changed from '{existingMovie.Movie}' to '{movieEvent.Movie}'");

            if (movieEvent.IMDb != existingMovie.IMDb)
            {
                var imdbStatus = string.IsNullOrEmpty(existingMovie.IMDb) ? "added" : "changed";
                changes.Add($"• IMDb Link {imdbStatus}");
            }

            if (movieEvent.PosterUrl != existingMovie.PosterUrl)
            {
                var posterStatus = string.IsNullOrEmpty(existingMovie.PosterUrl) ? "added" : "changed";
                changes.Add($"• Poster URL {posterStatus}");
            }

            if (movieEvent.Reasoning != existingMovie.Reasoning)
            {
                var reasonStatus = string.IsNullOrEmpty(existingMovie.Reasoning) ? "added" : "changed";
                changes.Add($"• Reasoning {reasonStatus}");
            }

            if (movieEvent.MeetupTime != existingMovie.MeetupTime)
            {
                var oldTime = existingMovie.MeetupTime?.ToString("MM/dd/yyyy h:mm tt") ?? "not set";
                var newTime = movieEvent.MeetupTime?.ToString("MM/dd/yyyy h:mm tt") ?? "not set";
                changes.Add($"• Meetup Time changed from {oldTime} to {newTime}");
            }

            if (movieEvent.AlreadySeen != existingMovie.AlreadySeen || movieEvent.SeenDate != existingMovie.SeenDate)
            {
                changes.Add(movieEvent.AlreadySeen
                    ? $"• Previously seen status changed to: seen in {movieEvent.SeenDate?.Year}"
                    : "• Previously seen status changed to: not seen");
            }

            if (!changes.Any()) return;

            var updateMessage = $"Movie {movieEvent.Movie} was changed by {movieEvent.Person}:\n" +
                               string.Join("\n", changes);

            AddSiteUpdate("MovieUpdated", updateMessage);
        }

        public List<StatsCommand> GetProcessedStatCommandsForMonth(string monthYear)
        {
            var filter = Builders<StatsCommand>.Filter.Eq("MonthYear", monthYear);

            return StatsCommands.Find(filter).ToList();
        }

        public List<StatsCommand> GetProcessedStatCommands()
        {
            return StatsCommands.Find(_ => true).ToList();
        }

        public void AddStatsCommand(StatsCommand commandResult)
        {
            StatsCommands?.InsertOne(commandResult);
        }

        public List<MovieEvent> GetAllMovieEvents(int? phaseNumber = null)
        {
            var sortDefinition = Builders<MovieEvent>.Sort.Ascending(me => me.StartDate);

            var filter = phaseNumber.HasValue
                ? Builders<MovieEvent>.Filter.Eq("PhaseNumber", phaseNumber.Value)
                : Builders<MovieEvent>.Filter.Empty;

            return MovieEvents.Find(filter)
                .Sort(sortDefinition)
                .ToList();
        }

        private List<Person> QueryPeople(bool respectOrder)
        {
            var sortDefinition = respectOrder
                ? Builders<Person>.Sort.Ascending(me => me.Order)
                : Builders<Person>.Sort.Ascending(me => me.Name);

            return People.Find(_ => true)
                .Sort(sortDefinition)
                .ToList();
        }

        public List<Person> GetAllPeople(bool respectOrder)
        {
            var people = QueryPeople(respectOrder);
            return people;
        }

        private List<Setting> QuerySetting()
        {
            return Settings.Find(_ => true).ToList();
        }

        public List<Setting> GetSettings()
        {
            var settings = QuerySetting();
            if (settings.Count == 0)
            {
                AddOrUpdateSetting(new Setting { Key = "StartDate", Value = (new DateTime(2024, 1, 1)).ToString() });
                AddOrUpdateSetting(new Setting { Key = "TimeCount", Value = "1" });
                AddOrUpdateSetting(new Setting { Key = "TimePeriod", Value = "Month" });

                settings = QuerySetting();
            }
            return settings;
        }

        internal void AddOrUpdateSetting(Setting setting)
        {
            var filter = Builders<Setting>.Filter.Eq("Id", setting.Id);
            var update = Builders<Setting>.Update
                .Set("Key", setting.Key)
                .Set("Value", setting.Value);
            Settings.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });
        }

        public void AddPerson(Person person)
        {
            People.InsertOne(person);
        }

        public void DeletePerson(Person person)
        {
            var filter = Builders<Person>.Filter.Eq("Id", person.Id);
            People.DeleteOne(filter);
        }

        internal void AddOrUpdatePerson(Person person)
        {
            var filter = Builders<Person>.Filter.Eq("Id", person.Id);
            var update = Builders<Person>.Update
                .Set("Name", person.Name)
                .Set("Order", person.Order);
            People.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });
        }

        internal Phase GetPhase(int phaseNumber, List<string> listNames, DateTime startDate)
        {
            var filter = Builders<Phase>.Filter.Eq("Number", phaseNumber);
            var foundPhase = Phases.Find(filter).FirstOrDefault();
            var endDate = startDate.AddMonths(listNames.Count).EndOfDay();
            //Minus a month so it adds a new phase 1 month before the current phase ends.
            var isCurrentPhase = DateTime.Now.IsWithinRange(startDate.AddMonths(-1), endDate);

            if (foundPhase == null)
            {
                foundPhase = new Phase { Number = phaseNumber, Events = new List<MovieEvent>(), People = string.Join(',', listNames), StartDate =  startDate, EndDate = endDate };
                if (!isCurrentPhase)
                    return foundPhase;
                Phases.InsertOne(foundPhase);
            }

            if (foundPhase.People != string.Join(',', listNames) && isCurrentPhase)
            {
                foundPhase.People = string.Join(',', listNames);
                foundPhase.EndDate = endDate;
                Phases.ReplaceOne(filter, foundPhase);
            }

            foundPhase = Phases.Find(filter).FirstOrDefault();
            foundPhase.Events = GetAllMovieEvents(phaseNumber);

            return foundPhase;
        }
    }
}
