using MongoDB.Driver;
using MovieReviewApp.Database;
using MovieReviewApp.Enums;
using MovieReviewApp.Models;

namespace MovieReviewApp.Services
{
    public class MovieReviewService
    {
        private readonly MongoDbService _db;

        public MovieReviewService(MongoDbService db)
        {
            _db = db;
        }

        public bool IsConnected => _db.IsConnected;

        #region Movie Events
        public List<MovieEvent> GetAllMovieEvents(int? phaseNumber = null)
        {
            if (phaseNumber.HasValue)
            {
                var filter = Builders<MovieEvent>.Filter.Eq(m => m.PhaseNumber, phaseNumber.Value);
                return _db.Find(CollectionType.MovieEvents, filter)
                    .OrderBy(me => me.StartDate)
                    .ToList();
            }

            return _db.GetAll<MovieEvent>(CollectionType.MovieEvents)
                .OrderBy(me => me.StartDate)
                .ToList();
        }

        public List<MovieEvent> GetPhaseEvents(int phaseNumber)
        {
            var filter = Builders<MovieEvent>.Filter.Eq(x => x.PhaseNumber, phaseNumber);
            return _db.Find(CollectionType.MovieEvents, filter)
                .OrderBy(x => x.StartDate)
                .ToList();
        }

        public MovieEvent? GetMovieEventById(Guid id)
        {
            var filter = Builders<MovieEvent>.Filter.Eq(x => x.Id, id);
            return _db.Find(CollectionType.MovieEvents, filter).FirstOrDefault();
        }

        public void AddOrUpdateMovieEvent(MovieEvent movieEvent)
        {
            var filter = Builders<MovieEvent>.Filter.Eq(m => m.Id, movieEvent.Id);
            var existingMovie = _db.Find(CollectionType.MovieEvents, filter).FirstOrDefault();
            var isNew = existingMovie == null || string.IsNullOrEmpty(existingMovie.Movie);

            var update = Builders<MovieEvent>.Update
                .Set(m => m.StartDate, movieEvent.StartDate)
                .Set(m => m.EndDate, movieEvent.EndDate)
                .Set(m => m.Person, movieEvent.Person)
                .Set(m => m.Movie, movieEvent.Movie)
                .Set(m => m.DownloadLink, movieEvent.DownloadLink)
                .Set(m => m.PosterUrl, movieEvent.PosterUrl)
                .Set(m => m.ImageId, movieEvent.ImageId)
                .Set(m => m.IMDb, movieEvent.IMDb)
                .Set(m => m.Reasoning, movieEvent.Reasoning)
                .Set(m => m.AlreadySeen, movieEvent.AlreadySeen)
                .Set(m => m.SeenDate, movieEvent.SeenDate)
                .Set(m => m.MeetupTime, movieEvent.MeetupTime?.ToLocalTime())
                .Set(m => m.PhaseNumber, movieEvent.PhaseNumber)
                .Set(m => m.Synopsis, movieEvent.Synopsis);

            _db.UpsertOne(CollectionType.MovieEvents, filter, update);

            if (!string.IsNullOrEmpty(movieEvent.Movie))
                CreateUpdateNotification(movieEvent, existingMovie, isNew);
        }
        #endregion

        #region People Management
        public List<Person> GetAllPeople(bool respectOrder)
        {
            var people = _db.GetAll<Person>(CollectionType.People);
            return respectOrder
                ? people.OrderBy(x => x.Order).ToList()
                : people.OrderBy(x => x.Name).ToList();
        }

        public void AddPerson(Person person) =>
            _db.InsertOne(CollectionType.People, person);

        public void DeletePerson(Person person)
        {
            var filter = Builders<Person>.Filter.Eq(x => x.Id, person.Id);
            _db.DeleteOne(CollectionType.People, filter);
        }

        public void AddOrUpdatePerson(Person person)
        {
            var filter = Builders<Person>.Filter.Eq(p => p.Id, person.Id);
            var update = Builders<Person>.Update
                .Set(p => p.Name, person.Name)
                .Set(p => p.Order, person.Order);

            _db.UpsertOne(CollectionType.People, filter, update);
        }
        #endregion

        #region Settings Management
        public List<Setting> GetSettings()
        {
            var settings = _db.GetAll<Setting>(CollectionType.Settings);
            if (settings.Count == 0)
            {
                AddOrUpdateSetting(new Setting { Key = "StartDate", Value = new DateTime(2024, 1, 1).ToString() });
                AddOrUpdateSetting(new Setting { Key = "TimeCount", Value = "1" });
                AddOrUpdateSetting(new Setting { Key = "TimePeriod", Value = "Month" });
                settings = _db.GetAll<Setting>(CollectionType.Settings);
            }
            return settings;
        }

        public void AddOrUpdateSetting(Setting setting)
        {
            var filter = Builders<Setting>.Filter.Eq(s => s.Key, setting.Key);
            var existingSetting = _db.Find(CollectionType.Settings, filter).FirstOrDefault();

            if (existingSetting != null)
            {
                setting.Id = existingSetting.Id;
                var update = Builders<Setting>.Update.Set(s => s.Value, setting.Value);
                _db.UpdateOne(CollectionType.Settings, filter, update);
            }
            else
            {
                _db.InsertOne(CollectionType.Settings, setting);
            }

            // Clean up duplicates
            var duplicates = _db.Find(CollectionType.Settings, filter)
                .Where(s => s.Id != (existingSetting?.Id ?? setting.Id))
                .ToList();

            foreach (var duplicate in duplicates)
            {
                var deleteFilter = Builders<Setting>.Filter.Eq(s => s.Id, duplicate.Id);
                _db.DeleteOne(CollectionType.Settings, deleteFilter);
            }
        }
        #endregion

        #region Phases
        public List<Phase> GetAllPhases()
        {
            var phases = _db.GetAll<Phase>(CollectionType.Phases)
                .OrderBy(p => p.Number)
                .ToList();

            // For each phase, get its events
            foreach (var phase in phases)
            {
                phase.Events = GetPhaseEvents(phase.Number);
            }

            return phases;
        }

        public void AddPhase(Phase phase) =>
            _db.InsertOne(CollectionType.Phases, phase);
        #endregion

        #region Stats Commands
        public List<StatsCommand> GetProcessedStatCommands() =>
            _db.GetAll<StatsCommand>(CollectionType.StatsCommands);

        public void AddStatsCommand(StatsCommand command) =>
            _db.InsertOne(CollectionType.StatsCommands, command);
        #endregion

        #region Site Updates
        public void AddSiteUpdate(string updateType, string description) =>
            _db.InsertOne(CollectionType.SiteUpdates, new SiteUpdate
            {
                LastUpdateTime = DateTime.UtcNow,
                UpdateType = updateType,
                Description = description
            });

        public DateTime? GetLatestUpdateTime()
        {
            var updates = _db.GetAll<SiteUpdate>(CollectionType.SiteUpdates)
                .OrderByDescending(x => x.LastUpdateTime)
                .ToList();

            return updates.FirstOrDefault()?.LastUpdateTime;
        }

        public List<SiteUpdate> GetRecentUpdates(DateTime since)
        {
            var filter = Builders<SiteUpdate>.Filter.Lt(x => x.LastUpdateTime, since);
            return _db.Find(CollectionType.SiteUpdates, filter)
                .OrderByDescending(x => x.LastUpdateTime)
                .ToList();
        }
        #endregion

        #region Award Questions
        public List<AwardQuestion> GetActiveAwardQuestions()
        {
            var filter = Builders<AwardQuestion>.Filter.Eq(x => x.IsActive, true);
            return _db.Find(CollectionType.AwardQuestions, filter);
        }

        public void AddOrUpdateAwardQuestion(AwardQuestion question)
        {
            var filter = Builders<AwardQuestion>.Filter.Eq(q => q.Id, question.Id);
            var update = Builders<AwardQuestion>.Update
                .Set(q => q.Question, question.Question)
                .Set(q => q.IsActive, question.IsActive)
                .Set(q => q.MaxVotes, question.MaxVotes);

            _db.UpsertOne(CollectionType.AwardQuestions, filter, update);
        }

        public void DeleteAwardQuestion(Guid questionId)
        {
            var filter = Builders<AwardQuestion>.Filter.Eq(x => x.Id, questionId);
            _db.DeleteOne(CollectionType.AwardQuestions, filter);
        }

        public void DeleteDefaultQuestions()
        {
            var filter = Builders<AwardQuestion>.Filter.Eq(x => x.Question, "New Question");
            _db.DeleteMany(CollectionType.AwardQuestions, filter);
        }
        #endregion

        #region Award Events
        public AwardEvent GetAwardEventById(Guid awardEventId) =>
            _db.GetById<AwardEvent>(CollectionType.AwardEvents, awardEventId);

        public void AddAwardEvent(AwardEvent awardEvent) =>
            _db.InsertOne(CollectionType.AwardEvents, awardEvent);

        public IEnumerable<AwardEvent> GetPastAwardEvents(Guid currentEventId)
        {
            try
            {
                var filter = Builders<AwardEvent>.Filter.Ne(e => e.Id, currentEventId);
                var results = _db.Find(CollectionType.AwardEvents, filter)
                    .OrderByDescending(e => e.EndDate)
                    .ToList();

                Console.WriteLine($"GetPastAwardEvents found {results.Count} past events");
                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetPastAwardEvents: {ex.Message}");
                return new List<AwardEvent>();
            }
        }

        public AwardEvent GetAwardEventByFilter(FilterDefinition<AwardEvent> filter)
        {
            try
            {
                var result = _db.Find(CollectionType.AwardEvents, filter).FirstOrDefault();
                if (result == null)
                {
                    Console.WriteLine("No award event found matching filter");
                }
                else
                {
                    Console.WriteLine($"Found award event: {result.Id}, {result.StartDate:yyyy-MM-dd}");
                }
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAwardEventByFilter: {ex.Message}");
                return null;
            }
        }

        public AwardEvent GetAwardEventForDate(DateTime date)
        {
            // First check if an award event already exists for this month
            var monthStart = new DateTime(date.Year, date.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var filter = Builders<AwardEvent>.Filter.And(
                Builders<AwardEvent>.Filter.Lte(e => e.StartDate, date),
                Builders<AwardEvent>.Filter.Gte(e => e.EndDate, date)
            );

            var awardEvent = _db.Find(CollectionType.AwardEvents, filter).FirstOrDefault();

            // If no award event exists and we meet all the criteria, create one
            if (awardEvent == null)
            {
                // Get latest phase number to determine if we need an award event
                var latestPhase = _db.GetAll<Phase>(CollectionType.Phases)
                    .OrderByDescending(p => p.Number)
                    .FirstOrDefault();
                if (latestPhase == null) return null;

                var awardSettings = GetAwardSettings();
                if (!awardSettings.AwardsEnabled) return null;

                bool isAwardMonth = latestPhase.Number % awardSettings.PhasesBeforeAward == 0;
                if (!isAwardMonth) return null;

                var awardMonthStart = latestPhase.EndDate.AddDays(1);
                var awardMonthEnd = awardMonthStart.AddMonths(1).AddDays(-1);

                // Only create if we're within 1 month of the award period starting
                var isWithinCreationWindow = DateProvider.Now >= awardMonthStart &&
                                           DateProvider.Now <= awardMonthEnd;
                if (!isWithinCreationWindow) return null;

                var questions = GetActiveAwardQuestions();
                if (!questions.Any()) return null;

                // Double-check one last time that no award event exists for this period
                var existingCheckFilter = Builders<AwardEvent>.Filter.And(
                    Builders<AwardEvent>.Filter.Gte(e => e.StartDate, awardMonthStart),
                    Builders<AwardEvent>.Filter.Lte(e => e.StartDate, awardMonthEnd)
                );

                var existingCheck = _db.Find(CollectionType.AwardEvents, existingCheckFilter).FirstOrDefault();
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
        #endregion

        #region Award Votes
        public async Task<List<AwardVote>> GetVotesForAwardEvent(Guid awardEventId)
        {
            var filter = Builders<AwardVote>.Filter.Eq(v => v.AwardEventId, awardEventId);
            return await _db.FindAsync(CollectionType.AwardVotes, filter);
        }

        public async Task<bool> AddVote(AwardVote vote)
        {
            try
            {
                // Check for existing votes for this movie in this question
                var existingVoteFilter = Builders<AwardVote>.Filter.And(
                    Builders<AwardVote>.Filter.Eq(v => v.AwardEventId, vote.AwardEventId),
                    Builders<AwardVote>.Filter.Eq(v => v.QuestionId, vote.QuestionId),
                    Builders<AwardVote>.Filter.Eq(v => v.MovieEventId, vote.MovieEventId),
                    Builders<AwardVote>.Filter.Eq(v => v.VoterName, vote.VoterName)
                );

                var existingVote = (await _db.FindAsync(CollectionType.AwardVotes, existingVoteFilter)).FirstOrDefault();
                if (existingVote != null)
                    return false;

                // Check if user has already used all their votes for this question
                var userVoteFilter = Builders<AwardVote>.Filter.And(
                    Builders<AwardVote>.Filter.Eq(v => v.AwardEventId, vote.AwardEventId),
                    Builders<AwardVote>.Filter.Eq(v => v.QuestionId, vote.QuestionId),
                    Builders<AwardVote>.Filter.Eq(v => v.VoterName, vote.VoterName)
                );

                var userVoteCount = await _db.CountAsync(CollectionType.AwardVotes, userVoteFilter);
                if (userVoteCount >= 3)
                    return false;

                await _db.InsertOneAsync(CollectionType.AwardVotes, vote);
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
                var voteFilter = Builders<AwardVote>.Filter.Eq(v => v.Id, voteId);
                var vote = (await _db.FindAsync(CollectionType.AwardVotes, voteFilter)).FirstOrDefault();
                if (vote == null) return false;

                var awardSettings = GetAwardSettings();
                if (!awardSettings.AllowVoteChanges)
                    return false;

                if (awardSettings.VoteChangeTimeLimit > 0)
                {
                    var voteAge = DateTime.UtcNow - vote.CreatedAt;
                    if (voteAge.TotalHours > awardSettings.VoteChangeTimeLimit)
                        return false;
                }

                // Delete all votes for this question by this user
                var deleteFilter = Builders<AwardVote>.Filter.And(
                    Builders<AwardVote>.Filter.Eq(v => v.AwardEventId, vote.AwardEventId),
                    Builders<AwardVote>.Filter.Eq(v => v.QuestionId, vote.QuestionId),
                    Builders<AwardVote>.Filter.Eq(v => v.VoterName, vote.VoterName)
                );

                var deletedCount = await _db.DeleteManyAsync(CollectionType.AwardVotes, deleteFilter);
                return deletedCount > 0;
            }
            catch
            {
                return false;
            }
        }

        public List<QuestionResult> GetQuestionResults(Guid awardEventId, Guid questionId)
        {
            var votesFilter = Builders<AwardVote>.Filter.And(
                Builders<AwardVote>.Filter.Eq(v => v.AwardEventId, awardEventId),
                Builders<AwardVote>.Filter.Eq(v => v.QuestionId, questionId)
            );
            var votes = _db.Find(CollectionType.AwardVotes, votesFilter);

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

        public async Task<List<string>> GetAvailableVoters(Guid awardEventId)
        {
            // Get all people from Persons table
            var allPeople = GetAllPeople(true)
                .Where(p => !string.IsNullOrEmpty(p.Name))
                .ToList();

            // Get active questions for this award event
            var awardEvent = await Task.FromResult(GetAwardEventById(awardEventId));
            if (awardEvent == null) return new List<string>();

            // Get all votes for this award event
            var allVotes = await GetVotesForAwardEvent(awardEventId);

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
            var awardEvent = GetAwardEventById(awardEventId);
            if (awardEvent == null) return new Dictionary<Guid, int>();

            var userVotes = await GetVotesForAwardEvent(awardEventId);
            var filteredUserVotes = userVotes.Where(v => v.VoterName == voterName).ToList();

            var votesPerQuestion = filteredUserVotes
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

        public async Task<List<(AwardQuestion Question, int RemainingVotes)>> GetAvailableQuestionsForUser(
            string voterName, Guid awardEventId)
        {
            var awardEvent = GetAwardEventById(awardEventId);
            if (awardEvent == null) return new List<(AwardQuestion, int)>();

            var awardSettings = GetAwardSettings();
            var questions = GetActiveAwardQuestions()
                .Where(q => awardEvent.Questions.Contains(q.Id))
                .ToList();

            // Get all votes for this user in this award event
            var userVotes = (await GetVotesForAwardEvent(awardEventId))
                .Where(v => v.VoterName == voterName)
                .ToList();

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
        #endregion

        #region Award Settings
        public AwardSetting GetAwardSettings()
        {
            var filter = Builders<Setting>.Filter.Eq(s => s.Key, "AwardSettings");
            var setting = _db.Find(CollectionType.Settings, filter).FirstOrDefault();

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
        #endregion

        #region Helper Methods
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
        #endregion
    }
}