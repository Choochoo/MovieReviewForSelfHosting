using MongoDB.Driver;
using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Database;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services
{
    public class MovieReviewService
    {
        private readonly IDatabaseService _db;

        public MovieReviewService(IDatabaseService db)
        {
            _db = db;
        }

        public bool IsConnected => _db.IsConnected;

        #region Movie Events
        public async Task<List<MovieEvent>> GetAllMovieEventsAsync(int? phaseNumber = null)
        {
            List<MovieEvent> events;
            if (phaseNumber.HasValue)
            {
                events = await _db.FindAsync<MovieEvent>(m => m.PhaseNumber == phaseNumber.Value);
            }
            else
            {
                events = await _db.GetAllAsync<MovieEvent>();
            }

            // Ensure IsEditing is always false when loading from database
            foreach (var movieEvent in events)
            {
                movieEvent.IsEditing = false;
            }

            return events.OrderBy(me => me.StartDate).ToList();
        }

        public List<MovieEvent> GetAllMovieEvents(int? phaseNumber = null)
        {
            return GetAllMovieEventsAsync(phaseNumber).GetAwaiter().GetResult();
        }

        public async Task<List<MovieEvent>> GetPhaseEventsAsync(int phaseNumber)
        {
            var events = await _db.FindAsync<MovieEvent>(x => x.PhaseNumber == phaseNumber);
            
            // Ensure IsEditing is always false when loading from database
            foreach (var movieEvent in events)
            {
                movieEvent.IsEditing = false;
            }
            
            return events.OrderBy(x => x.StartDate).ToList();
        }

        public List<MovieEvent> GetPhaseEvents(int phaseNumber)
        {
            return GetPhaseEventsAsync(phaseNumber).GetAwaiter().GetResult();
        }

        public async Task<MovieEvent?> GetMovieEventByIdAsync(Guid id)
        {
            var movieEvent = await _db.GetByIdAsync<MovieEvent>(id);
            if (movieEvent != null)
            {
                movieEvent.IsEditing = false;
            }
            return movieEvent;
        }

        public MovieEvent? GetMovieEventById(Guid id)
        {
            return GetMovieEventByIdAsync(id).GetAwaiter().GetResult();
        }

        public async Task AddOrUpdateMovieEventAsync(MovieEvent movieEvent)
        {
            await _db.UpsertAsync(movieEvent);
        }

        public void AddOrUpdateMovieEvent(MovieEvent movieEvent)
        {
            AddOrUpdateMovieEventAsync(movieEvent).GetAwaiter().GetResult();
        }
        #endregion

        #region People
        public async Task<List<Person>> GetAllPeopleAsync()
        {
            return await _db.GetAllAsync<Person>();
        }

        public List<Person> GetAllPeople()
        {
            return GetAllPeopleAsync().GetAwaiter().GetResult();
        }

        public async Task<List<Person>> GetAllPeopleAsync(bool respectOrder)
        {
            var people = await _db.GetAllAsync<Person>();
            return respectOrder
                ? people.OrderBy(p => p.Order).ToList()
                : people.ToList();
        }

        public List<Person> GetAllPeople(bool respectOrder)
        {
            return GetAllPeopleAsync(respectOrder).GetAwaiter().GetResult();
        }

        public async Task AddPersonAsync(Person person)
        {
            await _db.InsertAsync(person);
        }

        public void AddPerson(Person person)
        {
            AddPersonAsync(person).GetAwaiter().GetResult();
        }

        public async Task<bool> DeletePersonAsync(string name)
        {
            var people = await _db.FindAsync<Person>(p => p.Name == name);
            if (people.Any())
            {
                var person = people.First();
                return await _db.DeleteByIdAsync<Person>(person.Id);
            }
            return false;
        }

        public void DeletePerson(string name)
        {
            DeletePersonAsync(name).GetAwaiter().GetResult();
        }

        public async Task<bool> DeletePersonAsync(Person person)
        {
            return await _db.DeleteByIdAsync<Person>(person.Id);
        }

        public void DeletePerson(Person person)
        {
            DeletePersonAsync(person).GetAwaiter().GetResult();
        }

        public async Task UpdatePersonAsync(string oldName, Person updatedPerson)
        {
            var existing = await _db.FindOneAsync<Person>(p => p.Name == oldName);
            if (existing != null)
            {
                updatedPerson.Id = existing.Id; // Keep the same ID
                await _db.UpsertAsync(updatedPerson);
            }
        }

        public void UpdatePerson(string oldName, Person updatedPerson)
        {
            UpdatePersonAsync(oldName, updatedPerson).GetAwaiter().GetResult();
        }

        public async Task AddOrUpdatePersonAsync(Person person)
        {
            await _db.UpsertAsync(person);
        }

        public void AddOrUpdatePerson(Person person)
        {
            AddOrUpdatePersonAsync(person).GetAwaiter().GetResult();
        }
        #endregion

        #region Settings
        public async Task<List<Setting>> GetAllSettingsAsync()
        {
            return await _db.GetAllAsync<Setting>();
        }

        public async Task AddOrUpdateSettingAsync(Setting setting)
        {
            var existing = await _db.FindOneAsync<Setting>(s => s.Key == setting.Key);
            if (existing != null)
            {
                setting.Id = existing.Id; // Keep the same ID
            }
            await _db.UpsertAsync(setting);

            // Remove duplicates if any exist
            var duplicates = await _db.FindAsync<Setting>(s => s.Key == setting.Key);
            if (duplicates.Count > 1)
            {
                var toDelete = duplicates.Skip(1);
                foreach (var duplicate in toDelete)
                {
                    await _db.DeleteByIdAsync<Setting>(duplicate.Id);
                }
            }
        }

        public void AddOrUpdateSetting(Setting setting)
        {
            AddOrUpdateSettingAsync(setting).GetAwaiter().GetResult();
        }
        #endregion

        #region Phases
        public async Task<List<Phase>> GetPhasesAsync()
        {
            var phases = await _db.GetAllAsync<Phase>();
            return phases.OrderBy(p => p.Number).ToList();
        }

        public List<Phase> GetPhases()
        {
            return GetPhasesAsync().GetAwaiter().GetResult();
        }

        public async Task AddPhaseAsync(Phase phase)
        {
            await _db.InsertAsync(phase);
        }

        public void AddPhase(Phase phase)
        {
            AddPhaseAsync(phase).GetAwaiter().GetResult();
        }
        #endregion

        #region Site Updates
        public async Task AddSiteUpdateAsync(string description, string? username = null)
        {
            await _db.InsertAsync(new SiteUpdate
            {
                Description = description,
                UpdatedBy = username ?? "System",
                Timestamp = DateTime.UtcNow
            });
        }

        public void AddSiteUpdate(string description, string? username = null)
        {
            AddSiteUpdateAsync(description, username).GetAwaiter().GetResult();
        }

        public async Task<List<SiteUpdate>> GetRecentSiteUpdatesAsync(int count = 10)
        {
            var updates = await _db.GetAllAsync<SiteUpdate>();
            return updates.OrderByDescending(u => u.Timestamp).Take(count).ToList();
        }

        public List<SiteUpdate> GetRecentSiteUpdates(int count = 10)
        {
            return GetRecentSiteUpdatesAsync(count).GetAwaiter().GetResult();
        }

        public async Task<List<SiteUpdate>> GetSiteUpdatesByDateAsync(DateTime startDate, DateTime endDate)
        {
            return await _db.FindAsync<SiteUpdate>(u => u.Timestamp >= startDate && u.Timestamp <= endDate);
        }

        public List<SiteUpdate> GetSiteUpdatesByDate(DateTime startDate, DateTime endDate)
        {
            return GetSiteUpdatesByDateAsync(startDate, endDate).GetAwaiter().GetResult();
        }
        #endregion

        #region Award Questions
        public async Task<List<AwardQuestion>> GetAwardQuestionsAsync()
        {
            return await _db.GetAllAsync<AwardQuestion>();
        }

        public List<AwardQuestion> GetAwardQuestions()
        {
            return GetAwardQuestionsAsync().GetAwaiter().GetResult();
        }

        public async Task AddOrUpdateAwardQuestionAsync(AwardQuestion question)
        {
            var existing = await _db.FindOneAsync<AwardQuestion>(q => q.Id == question.Id);
            if (existing != null)
            {
                question.Id = existing.Id; // Keep the same ID
            }
            await _db.UpsertAsync(question);
        }

        public void AddOrUpdateAwardQuestion(AwardQuestion question)
        {
            AddOrUpdateAwardQuestionAsync(question).GetAwaiter().GetResult();
        }

        public async Task<bool> DeleteAwardQuestionAsync(string questionId)
        {
            return await _db.DeleteByIdAsync<AwardQuestion>(Guid.Parse(questionId));
        }

        public void DeleteAwardQuestion(string questionId)
        {
            DeleteAwardQuestionAsync(questionId).GetAwaiter().GetResult();
        }

        public async Task<long> DeleteAwardQuestionsAsync(List<string> questionIds)
        {
            long deletedCount = 0;
            foreach (var id in questionIds)
            {
                if (await _db.DeleteByIdAsync<AwardQuestion>(Guid.Parse(id)))
                {
                    deletedCount++;
                }
            }
            return deletedCount;
        }

        public void DeleteAwardQuestions(List<string> questionIds)
        {
            DeleteAwardQuestionsAsync(questionIds).GetAwaiter().GetResult();
        }
        #endregion

        #region Award Events
        public async Task<AwardEvent?> GetAwardEventAsync(Guid awardEventId)
        {
            return await _db.GetByIdAsync<AwardEvent>(awardEventId);
        }

        public AwardEvent? GetAwardEvent(Guid awardEventId)
        {
            return GetAwardEventAsync(awardEventId).GetAwaiter().GetResult();
        }

        public async Task CreateAwardEventAsync(AwardEvent awardEvent)
        {
            await _db.InsertAsync(awardEvent);
        }

        public void CreateAwardEvent(AwardEvent awardEvent)
        {
            CreateAwardEventAsync(awardEvent).GetAwaiter().GetResult();
        }

        public async Task<List<AwardEvent>> GetAwardEventsAsync(int? phaseNumber = null)
        {
            if (phaseNumber.HasValue)
            {
                var events = await _db.FindAsync<AwardEvent>(ae => ae.PhaseNumber == phaseNumber.Value);
                return events.OrderByDescending(ae => ae.CreatedDate).ToList();
            }

            var allEvents = await _db.GetAllAsync<AwardEvent>();
            return allEvents.OrderByDescending(ae => ae.CreatedDate).ToList();
        }

        public List<AwardEvent> GetAwardEvents(int? phaseNumber = null)
        {
            return GetAwardEventsAsync(phaseNumber).GetAwaiter().GetResult();
        }

        public async Task<AwardEvent?> GetCurrentOrUpcomingAwardEventAsync()
        {
            var now = DateTime.UtcNow;

            // First try to find a currently active event
            var activeEvent = await _db.FindOneAsync<AwardEvent>(ae =>
                ae.VotingStartDate <= now && ae.VotingEndDate >= now);

            if (activeEvent != null)
                return activeEvent;

            // If no active event, find the next upcoming one
            var upcomingEvents = await _db.FindAsync<AwardEvent>(ae => ae.VotingStartDate > now);
            return upcomingEvents.OrderBy(ae => ae.VotingStartDate).FirstOrDefault();
        }

        public AwardEvent? GetCurrentOrUpcomingAwardEvent()
        {
            return GetCurrentOrUpcomingAwardEventAsync().GetAwaiter().GetResult();
        }

        public async Task<bool> ShouldCreateAwardEventAsync()
        {
            var phases = await GetPhasesAsync();
            var latestPhase = phases.OrderByDescending(p => p.Number).FirstOrDefault();

            if (latestPhase == null)
                return false;

            var existingEvent = await _db.FindOneAsync<AwardEvent>(ae => ae.PhaseNumber == latestPhase.Number);
            return existingEvent == null;
        }

        public bool ShouldCreateAwardEvent()
        {
            return ShouldCreateAwardEventAsync().GetAwaiter().GetResult();
        }
        #endregion

        #region Award Votes
        public async Task<List<AwardVote>> GetVotesAsync(Guid awardEventId, Guid? questionId = null)
        {
            if (questionId.HasValue)
            {
                return await _db.FindAsync<AwardVote>(v => v.AwardEventId == awardEventId && v.QuestionId == questionId.Value);
            }

            return await _db.FindAsync<AwardVote>(v => v.AwardEventId == awardEventId);
        }

        public List<AwardVote> GetVotes(Guid awardEventId, Guid? questionId = null)
        {
            return GetVotesAsync(awardEventId, questionId).GetAwaiter().GetResult();
        }

        public async Task<bool> SubmitVoteAsync(AwardVote vote)
        {
            var existingVote = await _db.FindOneAsync<AwardVote>(v =>
                v.AwardEventId == vote.AwardEventId &&
                v.QuestionId == vote.QuestionId &&
                v.VoterName == vote.VoterName &&
                v.MovieEventId == vote.MovieEventId);

            if (existingVote != null)
            {
                return false; // Vote already exists
            }

            var userVoteCount = await _db.CountAsync<AwardVote>(v =>
                v.AwardEventId == vote.AwardEventId &&
                v.QuestionId == vote.QuestionId &&
                v.VoterName == vote.VoterName);

            if (userVoteCount >= 3)
            {
                return false; // User has already voted 3 times for this question
            }

            await _db.InsertAsync(vote);
            return true;
        }

        public bool SubmitVote(AwardVote vote)
        {
            return SubmitVoteAsync(vote).GetAwaiter().GetResult();
        }

        public async Task<bool> DeleteVoteAsync(Guid awardEventId, Guid questionId, string voterName, Guid movieEventId)
        {
            var vote = await _db.FindOneAsync<AwardVote>(v =>
                v.AwardEventId == awardEventId &&
                v.QuestionId == questionId &&
                v.VoterName == voterName &&
                v.MovieEventId == movieEventId);

            if (vote != null)
            {
                return await _db.DeleteByIdAsync<AwardVote>(vote.Id);
            }

            return false;
        }

        public bool DeleteVote(Guid awardEventId, Guid questionId, string voterName, Guid movieEventId)
        {
            return DeleteVoteAsync(awardEventId, questionId, voterName, movieEventId).GetAwaiter().GetResult();
        }

        public async Task<long> DeleteAllVotesForEventAsync(Guid awardEventId)
        {
            var votes = await _db.FindAsync<AwardVote>(v => v.AwardEventId == awardEventId);
            long deletedCount = 0;

            foreach (var vote in votes)
            {
                if (await _db.DeleteByIdAsync<AwardVote>(vote.Id))
                {
                    deletedCount++;
                }
            }

            return deletedCount;
        }

        public long DeleteAllVotesForEvent(Guid awardEventId)
        {
            return DeleteAllVotesForEventAsync(awardEventId).GetAwaiter().GetResult();
        }

        public async Task<Dictionary<Guid, int>> GetVoteCountsAsync(Guid awardEventId, Guid questionId)
        {
            var votes = await _db.FindAsync<AwardVote>(v => v.AwardEventId == awardEventId && v.QuestionId == questionId);
            return votes.GroupBy(v => v.MovieEventId)
                       .ToDictionary(g => g.Key, g => g.Count());
        }

        public Dictionary<Guid, int> GetVoteCounts(Guid awardEventId, Guid questionId)
        {
            return GetVoteCountsAsync(awardEventId, questionId).GetAwaiter().GetResult();
        }
        #endregion

        #region Additional Award Methods
        public async Task<AwardSetting> GetAwardSettingsAsync()
        {
            var setting = await GetSettingAsync("AwardSettings");
            if (setting?.Value != null)
            {
                return System.Text.Json.JsonSerializer.Deserialize<AwardSetting>(setting.Value) ?? new AwardSetting();
            }
            return new AwardSetting();
        }

        public AwardSetting GetAwardSettings()
        {
            return GetAwardSettingsAsync().GetAwaiter().GetResult();
        }

        public async Task<AwardEvent?> GetAwardEventForDateAsync(DateTime date)
        {
            var awardEvents = await _db.FindAsync<AwardEvent>(e => e.StartDate <= date && e.EndDate >= date);
            return awardEvents.FirstOrDefault();
        }

        public AwardEvent? GetAwardEventForDate(DateTime date)
        {
            return GetAwardEventForDateAsync(date).GetAwaiter().GetResult();
        }

        public async Task<List<AwardQuestion>> GetActiveAwardQuestionsAsync()
        {
            return await _db.FindAsync<AwardQuestion>(q => q.IsActive);
        }

        public List<AwardQuestion> GetActiveAwardQuestions()
        {
            return GetActiveAwardQuestionsAsync().GetAwaiter().GetResult();
        }

        public async Task<AwardEvent?> GetAwardEventByFilterAsync(FilterDefinition<AwardEvent> filter)
        {
            // Get all award events and filter in memory since MongoDbService doesn't have a direct FilterDefinition method
            var allEvents = await _db.GetAllAsync<AwardEvent>();
            // For now, just return the first event that matches basic criteria
            // This is a simplified implementation - in a real scenario, you'd want to implement proper FilterDefinition support
            return allEvents.FirstOrDefault();
        }

        public AwardEvent? GetAwardEventByFilter(FilterDefinition<AwardEvent> filter)
        {
            return GetAwardEventByFilterAsync(filter).GetAwaiter().GetResult();
        }

        public async Task<List<QuestionResult>> GetQuestionResultsAsync(Guid awardEventId, Guid questionId)
        {
            var votes = await _db.FindAsync<AwardVote>(v => v.AwardEventId == awardEventId && v.QuestionId == questionId);
            var events = await GetAllMovieEventsAsync();

            var results = votes
                .GroupBy(v => v.MovieEventId)
                .Select(g =>
                {
                    var movieEvent = events.FirstOrDefault(e => e.Id == g.Key);
                    var movieTitle = movieEvent?.Movie ?? "Unknown Movie";
                    var voteCounts = g.GroupBy(v => v.Points).ToDictionary(pg => pg.Key, pg => pg.Count());

                    return new QuestionResult
                    {
                        MovieTitle = movieTitle,
                        TotalPoints = g.Sum(v => v.Points),
                        FirstPlaceVotes = voteCounts.GetValueOrDefault(3, 0),
                        SecondPlaceVotes = voteCounts.GetValueOrDefault(2, 0),
                        ThirdPlaceVotes = voteCounts.GetValueOrDefault(1, 0)
                    };
                })
                .OrderByDescending(r => r.TotalPoints)
                .ToList();

            return results;
        }

        public List<QuestionResult> GetQuestionResults(Guid awardEventId, Guid questionId)
        {
            return GetQuestionResultsAsync(awardEventId, questionId).GetAwaiter().GetResult();
        }

        public async Task<List<SiteUpdate>> GetRecentUpdatesAsync(DateTime lastVisit)
        {
            return await _db.FindAsync<SiteUpdate>(u => u.Date > lastVisit);
        }

        public List<SiteUpdate> GetRecentUpdates(DateTime lastVisit)
        {
            return GetRecentUpdatesAsync(lastVisit).GetAwaiter().GetResult();
        }

        public async Task<List<Phase>> GetAllPhasesAsync()
        {
            var phases = await _db.GetAllAsync<Phase>();
            return phases.OrderBy(p => p.Number).ToList();
        }

        public List<Phase> GetAllPhases()
        {
            return GetAllPhasesAsync().GetAwaiter().GetResult();
        }

        public async Task<AwardEvent?> GetAwardEventByIdAsync(Guid awardEventId)
        {
            return await _db.GetByIdAsync<AwardEvent>(awardEventId);
        }

        public AwardEvent? GetAwardEventById(Guid awardEventId)
        {
            return GetAwardEventByIdAsync(awardEventId).GetAwaiter().GetResult();
        }

        public async Task DeleteDefaultQuestionsAsync()
        {
            // This method would delete default questions
            var defaultQuestions = await _db.FindAsync<AwardQuestion>(q => q.Question.Contains("Default"));
            foreach (var question in defaultQuestions)
            {
                await _db.DeleteByIdAsync<AwardQuestion>(question.Id);
            }
        }

        public void DeleteDefaultQuestions()
        {
            DeleteDefaultQuestionsAsync().GetAwaiter().GetResult();
        }

        public async Task DeleteAwardQuestionAsync(Guid questionId)
        {
            await _db.DeleteByIdAsync<AwardQuestion>(questionId);
        }

        public void DeleteAwardQuestion(Guid questionId)
        {
            DeleteAwardQuestionAsync(questionId).GetAwaiter().GetResult();
        }

        // Additional voting methods
        public async Task<List<AwardVote>> GetVotesForAwardEventAsync(Guid awardEventId)
        {
            return await GetVotesAsync(awardEventId);
        }

        public List<AwardVote> GetVotesForAwardEvent(Guid awardEventId)
        {
            return GetVotesForAwardEventAsync(awardEventId).GetAwaiter().GetResult();
        }

        public async Task<bool> AddVoteAsync(AwardVote vote)
        {
            await _db.InsertAsync(vote);
            return true;
        }

        public bool AddVote(AwardVote vote)
        {
            return AddVoteAsync(vote).GetAwaiter().GetResult();
        }

        public async Task<bool> RemoveVoteAsync(Guid voteId)
        {
            return await _db.DeleteByIdAsync<AwardVote>(voteId);
        }

        public bool RemoveVote(Guid voteId)
        {
            return RemoveVoteAsync(voteId).GetAwaiter().GetResult();
        }

        public async Task<List<string>> GetAvailableVotersAsync(Guid awardEventId)
        {
            var people = await GetAllPeopleAsync();
            return people.Select(p => p.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList();
        }

        public List<string> GetAvailableVoters(Guid awardEventId)
        {
            return GetAvailableVotersAsync(awardEventId).GetAwaiter().GetResult();
        }

        public async Task<Dictionary<Guid, int>> GetRemainingVotesForUserAsync(string userName, Guid awardEventId)
        {
            var votes = await GetVotesAsync(awardEventId);
            var userVotes = votes.Where(v => v.VoterName == userName);
            var questions = await GetActiveAwardQuestionsAsync();

            var result = new Dictionary<Guid, int>();
            foreach (var question in questions)
            {
                var voteCount = userVotes.Count(v => v.QuestionId == question.Id);
                result[question.Id] = Math.Max(0, question.MaxVotes - voteCount);
            }
            return result;
        }

        public Dictionary<Guid, int> GetRemainingVotesForUser(string userName, Guid awardEventId)
        {
            return GetRemainingVotesForUserAsync(userName, awardEventId).GetAwaiter().GetResult();
        }

        public async Task<List<(AwardQuestion Question, int RemainingVotes)>> GetAvailableQuestionsForUserAsync(string userName, Guid awardEventId)
        {
            var questions = await GetActiveAwardQuestionsAsync();
            var remainingVotes = await GetRemainingVotesForUserAsync(userName, awardEventId);

            return questions
                .Where(q => remainingVotes.GetValueOrDefault(q.Id, 0) > 0)
                .Select(q => (q, remainingVotes.GetValueOrDefault(q.Id, 0)))
                .ToList();
        }

        public List<(AwardQuestion Question, int RemainingVotes)> GetAvailableQuestionsForUser(string userName, Guid awardEventId)
        {
            return GetAvailableQuestionsForUserAsync(userName, awardEventId).GetAwaiter().GetResult();
        }

        public async Task<List<AwardEvent>> GetPastAwardEventsAsync(Guid currentEventId)
        {
            var allEvents = await _db.GetAllAsync<AwardEvent>();
            return allEvents.Where(e => e.Id != currentEventId && e.EndDate < DateTime.UtcNow).ToList();
        }

        public List<AwardEvent> GetPastAwardEvents(Guid currentEventId)
        {
            return GetPastAwardEventsAsync(currentEventId).GetAwaiter().GetResult();
        }
        #endregion

        #region Helper Methods
        public async Task<Setting?> GetSettingAsync(string key)
        {
            return await _db.FindOneAsync<Setting>(s => s.Key == key);
        }

        public Setting? GetSetting(string key)
        {
            return GetSettingAsync(key).GetAwaiter().GetResult();
        }
        #endregion
    }
}