using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MovieReviewApp.Application.Services;
using MovieReviewApp.Models;

namespace MovieReviewApp.Components.Partials
{
    public partial class AwardVoting
    {
        [Parameter]
        public DateTime CurrentDate { get; set; }

        [Inject]
        private AwardEventService AwardEventService { get; set; } = default!;

        [Inject]
        private AwardQuestionService AwardQuestionService { get; set; } = default!;

        [Inject]
        private PersonService PersonService { get; set; } = default!;

        [Inject]
        private AwardVoteService AwardVoteService { get; set; } = default!;

        [Inject]
        private MovieEventService MovieEventService { get; set; } = default!;

        [Inject]
        private IHttpContextAccessor HttpContextAccessor { get; set; } = default!;

        [Inject]
        private IJSRuntime JS { get; set; } = default!;

        private string? selectedUser;
        private string tempUserName = string.Empty;
        private bool meetupPassed = false;
        private AwardEvent? awardEvent;
        private List<AwardQuestion> questions = new();
        private List<MovieEvent> allMovies = new();
        private List<Person> allVoters = new();
        private List<Person> incompleteVoters = new();
        private Dictionary<string, int> voterVoteCounts = new();
        private List<string> existingUsers = new();
        private Dictionary<Guid, Guid> selectedMovies = new();
        private Dictionary<Guid, bool> showResultsDict = new();
        private Dictionary<Guid, string> passwordInputs = new();
        private Dictionary<Guid, bool> passwordErrors = new();
        private AwardSetting? awardSettings;
        private List<AwardVote> votes = new();
        private Dictionary<Guid, int> remainingVotes = new();
        private HashSet<Guid> verifiedQuestions = new();
        private bool showResults = false;
        private bool isPasswordVerified = false;
        private bool showPasswordError = false;
        private string enteredPassword = "";
        private List<AwardEvent> pastAwardEvents = new();
        private Dictionary<string, bool> showPastResultsDict = new();
        private const string RESULTS_PASSWORD = "006514";
        private Dictionary<(Guid EventId, Guid QuestionId), List<QuestionResult>> _cachedResults = new();
        private int _totalVoters;
        private int _totalPossiblePoints;

        private bool MeetupPassed =>
            awardEvent?.Questions.Any() == true &&
            allMovies.Any(m => m.MeetupTime.HasValue && m.MeetupTime.Value < DateTime.Now);

        protected override async Task OnInitializedAsync()
        {
            await LoadDataAsync();
            _totalVoters = (await PersonService.GetAllOrderedAsync(true)).Count;
            _totalPossiblePoints = _totalVoters * 3;
        }

        private async Task LoadDataAsync()
        {
            awardEvent = await AwardEventService.GetAwardEventForDateAsync(CurrentDate);
            if (awardEvent == null) return;

            // Get all voters and votes
            allVoters = await PersonService.GetAllOrderedAsync(true);
            votes = await AwardVoteService.GetVotesAsync(awardEvent.Id);

            // Group votes by voter and count them
            voterVoteCounts = votes
                .GroupBy(v => v.VoterName)
                .ToDictionary(g => g.Key, g => g.Count());

            // Calculate incomplete voters
            incompleteVoters = allVoters
                .Where(voter => !voterVoteCounts.ContainsKey(voter.Name) || voterVoteCounts[voter.Name] < 18)
                .OrderBy(voter => voter.Name)
                .ToList();

            // Always load questions
            questions = await AwardQuestionService.GetActiveAwardQuestionsAsync();

            // Get all eligible movies for the award event
            int currentPhaseNumber = GetCurrentPhaseNumber();
            allMovies = (await MovieEventService.GetAllAsync())
                .Where(m =>
                    m.PhaseNumber == currentPhaseNumber &&
                    !string.IsNullOrEmpty(m.Movie))
                .ToList();

            // Load past award events
            try
            {
                pastAwardEvents = (await AwardEventService.GetPastEventsAsync()).ToList();
                Console.WriteLine($"Found {pastAwardEvents.Count} past award events");
                foreach (AwardEvent past in pastAwardEvents)
                {
                    Console.WriteLine($"Past event: {past.Id} - {past.StartDate:MMM yyyy}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading past award events: {ex.Message}");
                pastAwardEvents = new List<AwardEvent>();
            }

            selectedUser = await GetStoredUser();
            await LoadUserData();

            // Initialize password inputs
            foreach (AwardQuestion question in questions)
            {
                passwordInputs[question.Id] = "";
            }
        }

        private async Task LoadUserData()
        {
            existingUsers = await AwardVoteService.GetAvailableVotersAsync(awardEvent.Id);

            if (!string.IsNullOrEmpty(selectedUser) && awardEvent != null)
            {
                remainingVotes = await AwardVoteService.GetRemainingVotesForUserAsync(
                    selectedUser,
                    awardEvent.Id,
                    questions
                );

                List<(AwardQuestion Question, int RemainingVotes)> availableQuestions = await AwardVoteService.GetAvailableQuestionsForUserAsync(selectedUser, awardEvent.Id);
                questions = availableQuestions.Select(x => x.Question).ToList();
                allMovies = await MovieEventService.GetAllAsync();
                allMovies = allMovies.Where(m => m.EndDate < awardEvent.EndDate && !string.IsNullOrEmpty(m.Movie))
                    .ToList();
                votes = await AwardVoteService.GetVotesAsync(awardEvent.Id);
                selectedMovies = questions.ToDictionary(q => q.Id, _ => Guid.Empty);
            }
        }

        private string GetUserIp()
        {
            return HttpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        private async Task HandleKeyPress(KeyboardEventArgs e, Guid questionId)
        {
            if (e.Key == "Enter")
            {
                await VerifyPassword(questionId);
            }
        }

        private async Task VerifyPassword(Guid categoryId)
        {
            if (!passwordInputs.ContainsKey(categoryId))
            {
                passwordInputs[categoryId] = "";
            }

            if (passwordInputs[categoryId] == RESULTS_PASSWORD)
            {
                _ = verifiedQuestions.Add(categoryId);
                passwordErrors[categoryId] = false;
            }
            else
            {
                passwordErrors[categoryId] = true;
            }
            passwordInputs[categoryId] = "";
        }

        private async Task CastVote(Guid questionId)
        {
            if (!selectedMovies.ContainsKey(questionId) ||
                selectedMovies[questionId] == Guid.Empty)
                return;
            _ = GetUserVotesForQuestion(questionId);
            int remainingVotesCount = GetRemainingVotesForQuestion(questionId);

            int points = remainingVotesCount == 3 ? 3 : remainingVotesCount == 2 ? 2 : 1;

            AwardVote vote = new AwardVote
            {
                AwardEventId = awardEvent.Id,
                QuestionId = questionId,
                MovieEventId = selectedMovies[questionId],
                VoterName = selectedUser,
                VoterIp = GetUserIp(),
                Points = points
            };

            AwardVote createdVote = await AwardVoteService.CreateAsync(vote);
            if (createdVote != null)
            {
                votes = await AwardVoteService.GetVotesAsync(awardEvent.Id);
                selectedMovies[questionId] = Guid.Empty;
                await LoadUserData();
                StateHasChanged();
            }
        }

        private async Task RemoveVote(Guid questionId, Guid voteId)
        {
            var confirmDelete = await JS.InvokeAsync<bool>("confirm", "Are you sure you want to remove this vote? You can cast a new vote after removing it.");
            if (!confirmDelete) return;

            if (await AwardVoteService.DeleteAsync(voteId))
            {
                votes = await AwardVoteService.GetVotesAsync(awardEvent.Id);
                await LoadUserData();
                StateHasChanged();
            }
        }

        private async Task<string> GetStoredUser()
        {
            try
            {
                return await JS.InvokeAsync<string>("localStorage.getItem", "awardVoterName") ?? "";
            }
            catch
            {
                return "";
            }
        }

        private async Task StoreUser(string username)
        {
            try
            {
                await JS.InvokeVoidAsync("localStorage.setItem", "awardVoterName", username);
            }
            catch
            {
                // Handle storage errors
            }
        }

        private List<AwardVote> GetUserVotesForQuestion(Guid questionId)
        {
            return votes.Where(v =>
                v.QuestionId == questionId &&
                v.VoterName == selectedUser)
                .OrderByDescending(v => v.Points)
                .ToList();
        }

        private int GetRemainingVotesForQuestion(Guid questionId)
        {
            return remainingVotes.GetValueOrDefault(questionId, 0);
        }

        private List<MovieEvent> GetAvailableMoviesForQuestion(Guid questionId)
        {
            List<Guid> votedMovieIds = GetUserVotesForQuestion(questionId)
                .Select(v => v.MovieEventId)
                .ToList();

            return allMovies
                .Where(m => !votedMovieIds.Contains(m.Id))
                .OrderBy(m => m.Movie)
                .ToList();
        }

        private bool IsShowingResults(Guid questionId) =>
            showResultsDict.ContainsKey(questionId);

        private bool IsVerified(Guid questionId) =>
            verifiedQuestions.Contains(questionId) || !IsCurrentAwardEvent();

        private void ToggleCategoryResults(Guid questionId)
        {
            showResultsDict[questionId] = !showResultsDict.GetValueOrDefault(questionId);
        }

        private int GetCurrentPhaseNumber()
        {
            List<MovieEvent> phases = MovieEventService.GetAllAsync().Result.OrderBy(p => p.PhaseNumber).ToList();
            MovieEvent? relevantPhase = phases
                .LastOrDefault(p => p.EndDate < awardEvent.StartDate);

            if (relevantPhase?.PhaseNumber != null)
                return relevantPhase.PhaseNumber.Value + 1;

            return 1;
        }

        private bool IsCurrentAwardEvent()
        {
            return awardEvent != null && awardEvent.EndDate >= DateTime.UtcNow;
        }

        private bool IsPastResultShowing(Guid eventId, Guid questionId)
        {
            var key = $"past_{eventId}_{questionId}";
            return showPastResultsDict.ContainsKey(key) && showPastResultsDict[key];
        }

        private void TogglePastResults(Guid eventId, Guid questionId)
        {
            var key = $"past_{eventId}_{questionId}";
            if (!showPastResultsDict.ContainsKey(key))
                showPastResultsDict[key] = false;

            showPastResultsDict[key] = !showPastResultsDict[key];
        }

        private bool IsEventResultShowing(Guid eventId, Guid questionId, bool isPastEvent)
        {
            if (isPastEvent)
            {
                var key = $"past_{eventId}_{questionId}";
                return showPastResultsDict.ContainsKey(key) && showPastResultsDict[key];
            }
            else
            {
                return IsShowingResults(questionId);
            }
        }

        private void ToggleEventResults(Guid eventId, Guid questionId, bool isPastEvent)
        {
            if (isPastEvent)
            {
                var key = $"past_{eventId}_{questionId}";
                if (!showPastResultsDict.ContainsKey(key))
                    showPastResultsDict[key] = false;

                showPastResultsDict[key] = !showPastResultsDict[key];
            }
            else
            {
                ToggleCategoryResults(questionId);
            }
        }

        private bool IsAuthorizedToSeeResults(Guid eventId, Guid questionId)
        {
            if (eventId != awardEvent.Id)
                return true;

            return verifiedQuestions.Contains(questionId) || !IsCurrentAwardEvent();
        }

        private List<AwardEvent> GetAllAwardEvents()
        {
            var allEvents = new List<AwardEvent> { awardEvent };
            allEvents.AddRange(pastAwardEvents);
            return allEvents;
        }

        private async Task SetUser()
        {
            if (string.IsNullOrWhiteSpace(tempUserName)) return;
            selectedUser = tempUserName;
            await StoreUser(selectedUser);
            await LoadUserData();
            tempUserName = "";
        }

        private async Task LogOut()
        {
            selectedUser = "";
            await JS.InvokeVoidAsync("localStorage.removeItem", "awardVoterName");
            await LoadUserData();
        }

        private void ToggleResults()
        {
            showResults = !showResults;
            if (!showResults)
            {
                isPasswordVerified = false;
                showPasswordError = false;
                enteredPassword = "";
            }
        }

        private async Task<List<QuestionResult>> GetQuestionResults(Guid eventId, Guid questionId)
        {
            var key = (eventId, questionId);
            if (!_cachedResults.ContainsKey(key))
            {
                _cachedResults[key] = (await AwardQuestionService.GetQuestionResultsAsync(eventId, questionId))
                    .OrderByDescending(r => r.TotalPoints)
                    .ToList();
            }
            return _cachedResults[key];
        }

        public int totalPossiblePoints => _totalPossiblePoints;
    }
}
