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
        private IMongoCollection<VoterProfile> VoterProfiles => database?.GetCollection<VoterProfile>("VoterProfiles");
        private IMongoCollection<VoteHistory> VoteHistory => database?.GetCollection<VoteHistory>("VoteHistory");

        public MongoDb(IConfiguration configuration)
        {
            string mongoEnvVariable = configuration.GetValue<string>("MongoEnvironmentVariable");
            string mongoUri = Environment.GetEnvironmentVariable(mongoEnvVariable);
            if (string.IsNullOrEmpty(mongoUri))
                throw new ArgumentNullException("MongoDB connection string not found in environment variables");

            var client = new MongoClient(mongoUri);
            database = client.GetDatabase("MovieReview");
        }

        public async Task<List<string>> GetAvailableVoters(Guid awardEventId)
        {
            // Get all people from Persons table
            var allPeople = GetAllPeople(true)
                .Where(p => !string.IsNullOrEmpty(p.Name))
                .ToList();

            // Get active questions for this award event
            var awardEvent = await AwardEvents.Find(e => e.Id == awardEventId).FirstOrDefaultAsync();
            if (awardEvent == null) return new List<string>();

            // Get all votes for this award event
            var allVotes = await AwardVotes
                .Find(v => v.AwardEventId == awardEventId)
                .ToListAsync();

            // Group votes by voter name and question
            var voterQuestionCounts = allVotes
                .GroupBy(v => new { v.VoterName, v.QuestionId })
                .ToDictionary(
                    g => (g.Key.VoterName, g.Key.QuestionId),
                    g => g.Count()
                );

            // Filter people who still have questions to vote on
            var availableVoters = allPeople
                .Where(person => HasRemainingVotes(person.Name, awardEvent.Questions, voterQuestionCounts))
                .Select(p => p.Name)
                .ToList();

            return availableVoters;
        }

        private bool HasRemainingVotes(string voterName, List<Guid> questionIds,
            Dictionary<(string VoterName, Guid QuestionId), int> voterQuestionCounts)
        {
            // Check each question to see if the voter has remaining votes
            foreach (var questionId in questionIds)
            {
                var votesUsed = voterQuestionCounts.GetValueOrDefault((voterName, questionId), 0);
                if (votesUsed < 3) // Less than 3 votes means they can still vote on this question
                {
                    return true;
                }
            }
            return false;
        }

        public async Task<Dictionary<Guid, int>> GetRemainingVotesForUser(string voterName, Guid awardEventId)
        {
            var awardEvent = await AwardEvents.Find(e => e.Id == awardEventId).FirstOrDefaultAsync();
            if (awardEvent == null) return new Dictionary<Guid, int>();

            var userVotes = await AwardVotes
                .Find(v => v.AwardEventId == awardEventId && v.VoterName == voterName)
                .ToListAsync();

            var votesPerQuestion = userVotes
                .GroupBy(v => v.QuestionId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Count()
                );

            return awardEvent.Questions.ToDictionary(
                questionId => questionId,
                questionId => 3 - votesPerQuestion.GetValueOrDefault(questionId, 0)
            );
        }

        // This can be called from your award voting component to get user-specific question information
        // In your MongoDb class
        public async Task<List<(AwardQuestion Question, int RemainingVotes)>> GetAvailableQuestionsForUser(
            string voterName, Guid awardEventId)
        {
            var awardEvent = await AwardEvents.Find(e => e.Id == awardEventId).FirstOrDefaultAsync();
            if (awardEvent == null) return new List<(AwardQuestion, int)>();

            var awardSettings = GetAwardSettings();
            var questions = GetActiveAwardQuestions()
                .Where(q => awardEvent.Questions.Contains(q.Id))
                .ToList();

            // Get all votes for this user in this award event
            var userVotes = await AwardVotes.Find(v =>
                v.AwardEventId == awardEventId &&
                v.VoterName == voterName)
                .ToListAsync();

            var results = new List<(AwardQuestion Question, int RemainingVotes)>();

            foreach (var question in questions)
            {
                var questionVotes = userVotes.Where(v => v.QuestionId == question.Id).ToList();
                var remainingVotes = question.MaxVotes - questionVotes.Count;

                // Show question if it has remaining votes OR has recent votes within 24-hour window
                var hasRecentVotes = questionVotes.Any(vote =>
                    (DateTime.UtcNow - vote.CreatedAt).TotalHours <= awardSettings.VoteChangeTimeLimit);

                if (remainingVotes > 0 || hasRecentVotes)
                {
                    results.Add((Question: question, RemainingVotes: remainingVotes));
                }
            }

            return results;
        }

        // Also let's double check GetActiveAwardQuestions is correct
        public List<AwardQuestion> GetActiveAwardQuestions() =>
            AwardQuestions.Find(x => x.IsActive).ToList();  // Make sure this isn't accidentally filtering anything

        public int GetTotalVoters() => (int)VoterProfiles.CountDocuments(_ => true);

        // Award Votes
        public async Task<List<AwardVote>> GetVotesForAwardEvent(Guid awardEventId)
        {
            return await AwardVotes.Find(v => v.AwardEventId == awardEventId).ToListAsync();
        }

        public async Task<bool> AddVote(AwardVote vote)
        {
            try
            {
                // Check for existing votes for this movie in this question
                var existingVote = await AwardVotes.Find(v =>
                    v.AwardEventId == vote.AwardEventId &&
                    v.QuestionId == vote.QuestionId &&
                    v.MovieEventId == vote.MovieEventId &&
                    v.VoterName == vote.VoterName
                ).FirstOrDefaultAsync();

                if (existingVote != null)
                    return false;

                // Check if user has already used all their votes for this question
                var userVoteCount = await AwardVotes.CountDocumentsAsync(v =>
                    v.AwardEventId == vote.AwardEventId &&
                    v.QuestionId == vote.QuestionId &&
                    v.VoterName == vote.VoterName);

                if (userVoteCount >= 3)
                    return false;

                await AwardVotes.InsertOneAsync(vote);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> RemoveVote(Guid voteId)
        {
            try
            {
                // First get the vote we're trying to remove to get its details
                var vote = await AwardVotes.Find(v => v.Id == voteId).FirstOrDefaultAsync();
                if (vote == null) return false;

                var awardSettings = GetAwardSettings();
                if (!awardSettings.AllowVoteChanges)
                {
                    return false;
                }

                if (awardSettings.VoteChangeTimeLimit > 0)
                {
                    var voteAge = DateTime.UtcNow - vote.CreatedAt;
                    if (voteAge.TotalHours > awardSettings.VoteChangeTimeLimit)
                    {
                        return false;
                    }
                }

                // Find all votes for this user in this question
                var allUserVotes = await AwardVotes.Find(v =>
                    v.AwardEventId == vote.AwardEventId &&
                    v.QuestionId == vote.QuestionId &&
                    v.VoterName == vote.VoterName)
                    .ToListAsync();

                // Create history records for all votes before deleting
                foreach (var voteToDelete in allUserVotes)
                {
                    var history = new VoteHistory
                    {
                        Id = Guid.NewGuid(),
                        AwardEventId = voteToDelete.AwardEventId,
                        QuestionId = voteToDelete.QuestionId,
                        MovieEventId = voteToDelete.MovieEventId,
                        VoterName = voteToDelete.VoterName,
                        Points = voteToDelete.Points,
                        CreatedAt = voteToDelete.CreatedAt,
                        DeletedAt = DateTime.UtcNow
                    };
                    await VoteHistory.InsertOneAsync(history);
                }

                // Delete all votes for this question
                var result = await AwardVotes.DeleteManyAsync(v =>
                    v.AwardEventId == vote.AwardEventId &&
                    v.QuestionId == vote.QuestionId &&
                    v.VoterName == vote.VoterName);

                return result.DeletedCount > 0;
            }
            catch
            {
                return false;
            }
        }

        public List<QuestionResult> GetQuestionResults(Guid awardEventId, Guid questionId)
        {
            var votes = AwardVotes.Find(v =>
                v.AwardEventId == awardEventId &&
                v.QuestionId == questionId).ToList();

            var movieEvents = GetAllMovieEvents().ToDictionary(m => m.Id);

            return votes
                .GroupBy(v => v.MovieEventId)
                .Select(g => new QuestionResult
                {
                    MovieTitle = movieEvents.GetValueOrDefault(g.Key)?.Movie ?? "Unknown Movie",
                    TotalPoints = g.Sum(v => v.Points),
                    FirstPlaceVotes = g.Count(v => v.Points == 3),
                    SecondPlaceVotes = g.Count(v => v.Points == 2),
                    ThirdPlaceVotes = g.Count(v => v.Points == 1)
                })
                .OrderByDescending(r => r.TotalPoints)
                .ToList();
        }

        // Award Events
        public AwardEvent GetAwardEventForDate(DateTime date)
        {
            // First check if an award event already exists for this month
            var monthStart = new DateTime(date.Year, date.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var filter = Builders<AwardEvent>.Filter.And(
                Builders<AwardEvent>.Filter.Lte(e => e.StartDate, date),
                Builders<AwardEvent>.Filter.Gte(e => e.EndDate, date)
            );

            var awardEvent = AwardEvents.Find(filter).FirstOrDefault();

            // If no award event exists and we meet all the criteria, create one
            if (awardEvent == null)
            {
                // Get latest phase number to determine if we need an award event
                var latestPhase = Phases.Find(_ => true)
                    .SortByDescending(p => p.Number)
                    .FirstOrDefault();
                if (latestPhase == null) return null;

                var awardSettings = GetAwardSettings();
                if (!awardSettings.AwardsEnabled) return null;

                bool isAwardMonth = latestPhase.Number % awardSettings.PhasesBeforeAward == 0;
                if (!isAwardMonth) return null;

                var awardMonthStart = latestPhase.EndDate.AddDays(1);
                var awardMonthEnd = awardMonthStart.AddMonths(1).AddDays(-1);

                // Only create if we're within 1 month of the award period starting
                var isWithinCreationWindow = DateProvider.Now >= awardMonthStart.AddMonths(-1) &&
                                           DateProvider.Now <= awardMonthEnd;
                if (!isWithinCreationWindow) return null;

                var questions = GetActiveAwardQuestions();
                if (!questions.Any()) return null;

                // Double-check one last time that no award event exists for this period
                // This prevents race conditions in case multiple requests try to create at once
                var existingCheck = AwardEvents.Find(Builders<AwardEvent>.Filter.And(
                    Builders<AwardEvent>.Filter.Gte(e => e.StartDate, awardMonthStart),
                    Builders<AwardEvent>.Filter.Lte(e => e.StartDate, awardMonthEnd)
                )).FirstOrDefault();

                if (existingCheck != null)
                {
                    return existingCheck;
                }

                awardEvent = new AwardEvent
                {
                    StartDate = awardMonthStart,
                    EndDate = awardMonthEnd,
                    Questions = questions.Select(q => q.Id).ToList()
                };
                AddAwardEvent(awardEvent);
            }

            return awardEvent;
        }

        public void AddAwardEvent(AwardEvent awardEvent) =>
            AwardEvents.InsertOne(awardEvent);

        // Award Questions
        public void AddOrUpdateAwardQuestion(AwardQuestion question)
        {
            var filter = Builders<AwardQuestion>.Filter.Eq(q => q.Id, question.Id);
            var update = Builders<AwardQuestion>.Update
                .Set(q => q.Question, question.Question)
                .Set(q => q.IsActive, question.IsActive)
                .Set(q => q.MaxVotes, question.MaxVotes);
            AwardQuestions.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });
        }


        public void DeleteAwardQuestion(Guid questionId) =>
            AwardQuestions.DeleteOne(x => x.Id == questionId);

        public void DeleteDefaultQuestions() =>
            AwardQuestions.DeleteMany(x => x.Question == "New Question");

        // Award Settings
        public AwardSetting GetAwardSettings()
        {
            var filter = Builders<Setting>.Filter.Eq(s => s.Key, "AwardSettings");
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

        // Movie Events
        public List<MovieEvent> GetAllMovieEvents(int? phaseNumber = null)
        {
            var filter = phaseNumber.HasValue
                ? Builders<MovieEvent>.Filter.Eq(m => m.PhaseNumber, phaseNumber.Value)
                : Builders<MovieEvent>.Filter.Empty;

            return MovieEvents.Find(filter)
                .SortBy(me => me.StartDate)
                .ToList();
        }

        public List<MovieEvent> GetPhaseEvents(int phaseNumber) =>
            MovieEvents.Find(x => x.PhaseNumber == phaseNumber)
                .SortBy(x => x.StartDate)
                .ToList();

        public void AddOrUpdateMovieEvent(MovieEvent movieEvent)
        {
            var filter = Builders<MovieEvent>.Filter.Eq(m => m.Id, movieEvent.Id);
            var existingMovie = MovieEvents.Find(filter).FirstOrDefault();
            var isNew = existingMovie == null || string.IsNullOrEmpty(existingMovie.Movie);

            var update = Builders<MovieEvent>.Update
                .Set(m => m.StartDate, movieEvent.StartDate)
                .Set(m => m.EndDate, movieEvent.EndDate)
                .Set(m => m.Person, movieEvent.Person)
                .Set(m => m.Movie, movieEvent.Movie)
                .Set(m => m.DownloadLink, movieEvent.DownloadLink)
                .Set(m => m.PosterUrl, movieEvent.PosterUrl)
                .Set(m => m.IMDb, movieEvent.IMDb)
                .Set(m => m.Reasoning, movieEvent.Reasoning)
                .Set(m => m.AlreadySeen, movieEvent.AlreadySeen)
                .Set(m => m.SeenDate, movieEvent.SeenDate)
                .Set(m => m.MeetupTime, movieEvent.MeetupTime?.ToLocalTime())
                .Set(m => m.PhaseNumber, movieEvent.PhaseNumber)
                .Set(m => m.Synopsis, movieEvent.Synopsis);

            MovieEvents.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });

            if (!string.IsNullOrEmpty(movieEvent.Movie))
                CreateUpdateNotification(movieEvent, existingMovie, isNew);
        }

        // Site Updates
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

        // People Management
        public List<Person> GetAllPeople(bool respectOrder) =>
            People.Find(_ => true)
                .SortBy(x => respectOrder ? x.Order : x.Name)
                .ToList();

        public void AddPerson(Person person) =>
            People.InsertOne(person);

        public void DeletePerson(Person person) =>
            People.DeleteOne(x => x.Id == person.Id);

        public void AddOrUpdatePerson(Person person)
        {
            var filter = Builders<Person>.Filter.Eq(p => p.Id, person.Id);
            var update = Builders<Person>.Update
                .Set(p => p.Name, person.Name)
                .Set(p => p.Order, person.Order);
            People.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });
        }

        // Settings Management
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
            var filter = Builders<Setting>.Filter.Eq(s => s.Key, setting.Key);
            var existingSetting = Settings.Find(filter).FirstOrDefault();

            if (existingSetting != null)
            {
                setting.Id = existingSetting.Id;
                Settings.UpdateOne(filter, Builders<Setting>.Update.Set(s => s.Value, setting.Value));
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
                Settings.DeleteOne(Builders<Setting>.Filter.Eq(s => s.Id, duplicate.Id));
        }

        // Stats Commands
        public List<StatsCommand> GetProcessedStatCommands() =>
            StatsCommands.Find(_ => true).ToList();

        public void AddStatsCommand(StatsCommand command) =>
            StatsCommands?.InsertOne(command);
    }

    // Model for voter profiles
    public class VoterProfile : BaseModel
    {
        public string Name { get; set; }
        public List<string> KnownIps { get; set; } = new();
    }
}