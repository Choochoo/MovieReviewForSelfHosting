using MongoDB.Driver;
using MovieReviewApp.Extensions;
using MovieReviewApp.Models;
using MovieReviewApp.Services;

namespace MovieReviewApp.Database
{
    public class MongoDb
    {
        private readonly IMongoDatabase? database;
        private IMongoCollection<Phase>? Phases => database?.GetCollection<Phase>("Phases");
        private IMongoCollection<MovieEvent>? MovieEvents => database?.GetCollection<MovieEvent>("MovieReviews");
        private IMongoCollection<Person>? People => database?.GetCollection<Person>("People");
        private IMongoCollection<Setting>? Settings => database?.GetCollection<Setting>("Settings");
        private IMongoCollection<StatsCommand>? StatsCommands => database?.GetCollection<StatsCommand>("StatsCommands");
        private IMongoCollection<SiteUpdate> SiteUpdates => database?.GetCollection<SiteUpdate>("SiteUpdates");
        private IMongoCollection<AwardQuestion> AwardQuestions => database?.GetCollection<AwardQuestion>("AwardQuestions");
        private IMongoCollection<AwardEvent> AwardEvents => database?.GetCollection<AwardEvent>("AwardEvents");
        private IMongoCollection<AwardVote> AwardVotes => database?.GetCollection<AwardVote>("AwardVotes");

        public MongoDb(IConfiguration configuration)
        {
            string mongoEnvVariable = configuration.GetValue<string>("MongoEnvironmentVariable");
            string mongoUri = Environment.GetEnvironmentVariable(mongoEnvVariable);
            if (string.IsNullOrEmpty(mongoUri))
                throw new ArgumentNullException("MongoDB connection string not found in environment variables");

            var client = new MongoClient(mongoUri);
            database = client.GetDatabase("MovieReview");
        }

        public List<VoteResult> GetQuestionResults(string awardEventId, string questionId)
        {
            var votes = AwardVotes.Find(v =>
                v.AwardEventId == awardEventId &&
                v.QuestionId == questionId)
                .ToList();

            var movieEvents = MovieEvents.Find(Builders<MovieEvent>.Filter.Empty).ToList()
                .ToDictionary(m => m.Id);

            return votes
                .GroupBy(v => v.MovieEventId)
                .Select(g => new VoteResult
                {
                    MovieEventId = g.Key,
                    MovieTitle = movieEvents.TryGetValue(Guid.Parse(g.Key), out var movie) ? movie.Movie : "Unknown",
                    TotalPoints = g.Sum(v => v.GetPoints()),
                    FirstPlaceVotes = g.Count(v => v.VoteOrder == 1),
                    SecondPlaceVotes = g.Count(v => v.VoteOrder == 2),
                    ThirdPlaceVotes = g.Count(v => v.VoteOrder == 3)
                })
                .OrderByDescending(r => r.TotalPoints)
                .ThenByDescending(r => r.FirstPlaceVotes)
                .ToList();
        }

        public async Task<bool> AddVote(AwardVote vote)
        {
            var existingVotes = await AwardVotes
                .Find(v => v.AwardEventId == vote.AwardEventId &&
                        v.QuestionId == vote.QuestionId &&
                        v.VoterIp == vote.VoterIp)
                .ToListAsync();

            if (existingVotes.Count >= 3)
                return false;

            vote.VoteOrder = existingVotes.Count + 1;
            vote.VoteDate = DateTime.UtcNow;
            await AwardVotes.InsertOneAsync(vote);
            return true;
        }

        public AwardEvent GetAwardEventForDate(DateTime date)
        {
            var filter = Builders<AwardEvent>.Filter.And(
                Builders<AwardEvent>.Filter.Lte("StartDate", date),
                Builders<AwardEvent>.Filter.Gte("EndDate", date)
            );

            var awardEvent = AwardEvents.Find(filter).FirstOrDefault();
            if (awardEvent == null)
            {
                // Get latest phase number to determine if we need an award event
                var latestPhase = Phases.Find(_ => true)
                    .SortByDescending(p => p.Number)
                    .FirstOrDefault();

                if (latestPhase == null) return null;

                var awardSettings = GetAwardSettings();
                if (!awardSettings.AwardsEnabled) return null;

                // Check if we're at an award month boundary
                bool isAwardMonth = latestPhase.Number % awardSettings.PhasesBeforeAward == 0;
                if (!isAwardMonth) return null;

                // Only create if we're in the award month or within a month of it
                var awardMonthStart = latestPhase.EndDate.AddDays(1);
                var awardMonthEnd = awardMonthStart.AddMonths(1);
                var isWithinAwardPeriod = date >= awardMonthStart.AddMonths(-1) &&
                                         date <= awardMonthEnd;

                if (!isWithinAwardPeriod) return null;

                var questions = GetActiveAwardQuestions();
                if (!questions.Any()) return null;

                awardEvent = new AwardEvent
                {
                    StartDate = awardMonthStart,
                    EndDate = awardMonthEnd.AddDays(-1),
                    IsActive = true,
                    Questions = questions.Select(q => q.Id).ToList()
                };

                AddAwardEvent(awardEvent);
            }

            return awardEvent;
        }

        public void AddOrUpdateAwardQuestion(AwardQuestion question)
        {
            var filter = Builders<AwardQuestion>.Filter.Eq("Id", question.Id);
            var update = Builders<AwardQuestion>.Update
                .Set("Question", question.Question)
                .Set("IsActive", question.IsActive)
                .Set("MaxVotes", question.MaxVotes);
            AwardQuestions.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });
        }

        public List<AwardQuestion> GetActiveAwardQuestions() =>
            AwardQuestions.Find(x => x.IsActive).ToList();

        public void AddAwardEvent(AwardEvent awardEvent) =>
            AwardEvents.InsertOne(awardEvent);

        public List<AwardVote> GetVotesForAwardEvent(string awardEventId) =>
            AwardVotes.Find(v => v.AwardEventId == awardEventId).ToList();

        public int GetVoteCountForIp(string awardEventId, string questionId, string voterIp) =>
            (int)AwardVotes.CountDocuments(v =>
                v.AwardEventId == awardEventId &&
                v.QuestionId == questionId &&
                v.VoterIp == voterIp);

        public AwardSetting GetAwardSettings()
        {
            var filter = Builders<Setting>.Filter.Eq("Key", "AwardSettings");
            var setting = Settings.Find(filter).FirstOrDefault();

            if (setting == null)
            {
                var defaultSettings = new AwardSetting
                {
                    AwardsEnabled = false,
                    PhasesBeforeAward = 2
                };
                AddOrUpdateSetting(new Setting
                {
                    Key = "AwardSettings",
                    Value = System.Text.Json.JsonSerializer.Serialize(defaultSettings)
                });
                return defaultSettings;
            }

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<AwardSetting>(setting.Value)
                       ?? new AwardSetting();
            }
            catch
            {
                return new AwardSetting();
            }
        }

        public List<MovieEvent> GetAllMovieEvents(int? phaseNumber = null)
        {
            var filter = phaseNumber.HasValue
                ? Builders<MovieEvent>.Filter.Eq("PhaseNumber", phaseNumber.Value)
                : Builders<MovieEvent>.Filter.Empty;

            return MovieEvents.Find(filter)
                .SortBy(me => me.StartDate)
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

            if (!string.IsNullOrEmpty(movieEvent.Movie))
                CreateUpdateNotification(movieEvent, existingMovie, isNew);
        }

        private void CreateUpdateNotification(MovieEvent movieEvent, MovieEvent existingMovie, bool isNew)
        {
            if (isNew)
            {
                var changes = new List<string> { $"• Movie: {movieEvent.Movie}" };
                if (!string.IsNullOrEmpty(movieEvent.IMDb)) changes.Add("• IMDb Link Added");
                if (!string.IsNullOrEmpty(movieEvent.PosterUrl)) changes.Add("• Poster Added");
                if (!string.IsNullOrEmpty(movieEvent.Reasoning)) changes.Add("• Reasoning Added");
                if (movieEvent.MeetupTime.HasValue)
                    changes.Add($"• Meetup Time: {movieEvent.MeetupTime.Value:MM/dd/yyyy h:mm tt}");
                changes.Add(movieEvent.AlreadySeen
                    ? $"• Previously seen in {movieEvent.SeenDate?.Year}"
                    : "• Not previously seen");

                AddSiteUpdate("MovieAdded",
                    $"New movie added for {movieEvent.StartDate:MMMM yyyy} by {movieEvent.Person}:\n" +
                    string.Join("\n", changes));
                return;
            }

            if (existingMovie == null) return;

            var updates = new List<string>();
            if (movieEvent.Movie != existingMovie.Movie)
                updates.Add($"• Movie changed from '{existingMovie.Movie}' to '{movieEvent.Movie}'");
            if (movieEvent.IMDb != existingMovie.IMDb)
                updates.Add($"• IMDb Link {(string.IsNullOrEmpty(existingMovie.IMDb) ? "added" : "changed")}");
            if (movieEvent.PosterUrl != existingMovie.PosterUrl)
                updates.Add($"• Poster URL {(string.IsNullOrEmpty(existingMovie.PosterUrl) ? "added" : "changed")}");
            if (movieEvent.Reasoning != existingMovie.Reasoning)
                updates.Add($"• Reasoning {(string.IsNullOrEmpty(existingMovie.Reasoning) ? "added" : "changed")}");
            if (movieEvent.MeetupTime != existingMovie.MeetupTime)
                updates.Add($"• Meetup Time changed from {existingMovie.MeetupTime?.ToString("MM/dd/yyyy h:mm tt") ?? "not set"} to {movieEvent.MeetupTime?.ToString("MM/dd/yyyy h:mm tt") ?? "not set"}");
            if (movieEvent.AlreadySeen != existingMovie.AlreadySeen || movieEvent.SeenDate != existingMovie.SeenDate)
                updates.Add(movieEvent.AlreadySeen
                    ? $"• Previously seen status changed to: seen in {movieEvent.SeenDate?.Year}"
                    : "• Previously seen status changed to: not seen");

            if (updates.Any())
                AddSiteUpdate("MovieUpdated",
                    $"Movie {movieEvent.Movie} was changed by {movieEvent.Person}:\n" +
                    string.Join("\n", updates));
        }

        public void AddSiteUpdate(string updateType, string description) =>
            SiteUpdates.InsertOne(new SiteUpdate
            {
                LastUpdateTime = DateTime.UtcNow,
                UpdateType = updateType,
                Description = description
            });

        public DateTime? GetLatestUpdateTime() =>
            SiteUpdates.Find(_ => true)
                .SortByDescending(x => x.LastUpdateTime)
                .FirstOrDefault()?.LastUpdateTime;

        public List<SiteUpdate> GetRecentUpdates(DateTime since) =>
            SiteUpdates.Find(x => x.LastUpdateTime < since)
                .SortByDescending(x => x.LastUpdateTime)
                .ToList();

        public List<Person> GetAllPeople(bool respectOrder) =>
            People.Find(_ => true)
                .SortBy(x => respectOrder ? x.Order : x.Name)
                .ToList();

        public List<Setting> GetSettings()
        {
            var settings = Settings.Find(_ => true).ToList();
            if (settings.Count == 0)
            {
                AddOrUpdateSetting(new Setting { Key = "StartDate", Value = new DateTime(2024, 1, 1).ToString() });
                AddOrUpdateSetting(new Setting { Key = "TimeCount", Value = "1" });
                AddOrUpdateSetting(new Setting { Key = "TimePeriod", Value = "Month" });
                settings = Settings.Find(_ => true).ToList();
            }
            return settings;
        }

        public void AddOrUpdateSetting(Setting setting)
        {
            var filter = Builders<Setting>.Filter.Eq("Key", setting.Key);
            var existingSetting = Settings.Find(filter).FirstOrDefault();

            if (existingSetting != null)
            {
                setting.Id = existingSetting.Id;
                Settings.UpdateOne(filter, Builders<Setting>.Update.Set("Value", setting.Value));
            }
            else
            {
                Settings.InsertOne(setting);
            }

            // Clean up duplicates
            var duplicates = Settings.Find(filter).ToList()
                .Where(s => s.Id != (existingSetting?.Id ?? setting.Id))
                .ToList();

            foreach (var duplicate in duplicates)
                Settings.DeleteOne(Builders<Setting>.Filter.Eq("_id", duplicate.Id));
        }

        public void DeleteDefaultQuestions() =>
            AwardQuestions.DeleteMany(x => x.Question == "New Question");

        public void AddPerson(Person person) =>
            People.InsertOne(person);

        public void DeletePerson(Person person) =>
            People.DeleteOne(x => x.Id == person.Id);

        public void AddOrUpdatePerson(Person person)
        {
            var filter = Builders<Person>.Filter.Eq("Id", person.Id);
            var update = Builders<Person>.Update
                .Set("Name", person.Name)
                .Set("Order", person.Order);
            People.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });
        }

        public void DeleteAwardQuestion(string questionId) =>
            AwardQuestions.DeleteOne(x => x.Id == questionId);

        public List<MovieEvent> GetPhaseEvents(int phaseNumber) =>
            MovieEvents.Find(x => x.PhaseNumber == phaseNumber)
                .SortBy(x => x.StartDate)
                .ToList();

        public List<StatsCommand> GetProcessedStatCommands() =>
            StatsCommands.Find(_ => true).ToList();

        public void AddStatsCommand(StatsCommand command) =>
            StatsCommands?.InsertOne(command);
    }
}