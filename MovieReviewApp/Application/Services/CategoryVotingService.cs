using MovieReviewApp.Extensions;
using MovieReviewApp.Models;
using MovieReviewApp.Utilities;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Orchestration service for category voting functionality.
/// Coordinates between event, vote, identity, and generation services.
/// </summary>
public class CategoryVotingService
{
    private readonly CategoryVotingEventService _eventService;
    private readonly CategoryVoteService _voteService;
    private readonly VoterIdentityService _identityService;
    private readonly CategoryGenerationService _generationService;
    private readonly SettingService _settingService;
    private readonly PersonService _personService;
    private readonly ILogger<CategoryVotingService> _logger;

    public CategoryVotingService(
        CategoryVotingEventService eventService,
        CategoryVoteService voteService,
        VoterIdentityService identityService,
        CategoryGenerationService generationService,
        SettingService settingService,
        PersonService personService,
        ILogger<CategoryVotingService> logger)
    {
        _eventService = eventService;
        _voteService = voteService;
        _identityService = identityService;
        _generationService = generationService;
        _settingService = settingService;
        _personService = personService;
        _logger = logger;
    }

    #region Detection Methods

    /// <summary>
    /// Checks if the current month is a pre-awards voting month
    /// </summary>
    public async Task<bool> IsPreAwardsMonthAsync()
    {
        return await IsPreAwardsMonthAsync(DateProvider.Now);
    }

    /// <summary>
    /// Checks if a specific month is a pre-awards voting month
    /// </summary>
    public async Task<bool> IsPreAwardsMonthAsync(DateTime targetMonth)
    {
        _logger.LogInformation("IsPreAwardsMonthAsync: Checking for targetMonth={TargetMonth}", targetMonth);

        AwardSetting? awardSettings = await _settingService.GetAwardSettingsAsync();
        if (awardSettings?.AwardsEnabled != true)
        {
            _logger.LogInformation("IsPreAwardsMonthAsync: Awards not enabled. AwardsEnabled={Enabled}",
                awardSettings?.AwardsEnabled);
            return false;
        }

        _logger.LogInformation("IsPreAwardsMonthAsync: Awards enabled. PhasesBeforeAward={Phases}",
            awardSettings.PhasesBeforeAward);

        List<Setting> settings = await _settingService.GetAllAsync();
        Setting? startDateSetting = settings.FirstOrDefault(s => s.Key == "StartDate");

        if (startDateSetting == null || !DateTime.TryParse(startDateSetting.Value, out DateTime clubStartDate))
        {
            _logger.LogInformation("IsPreAwardsMonthAsync: StartDate not found or invalid. Value={Value}",
                startDateSetting?.Value);
            return false;
        }

        _logger.LogInformation("IsPreAwardsMonthAsync: ClubStartDate={StartDate}", clubStartDate);

        List<Person> people = await _personService.GetAllAsync();
        int peoplePerPhase = people.Count > 0 ? people.Count : 6;

        _logger.LogInformation("IsPreAwardsMonthAsync: PeopleCount={Count}", peoplePerPhase);

        // Also check if NEXT month is awards month for debugging
        DateTime nextMonth = targetMonth.AddMonths(1);
        bool isNextMonthAwards = PhaseCalculator.IsAwardsMonth(nextMonth, clubStartDate, peoplePerPhase, awardSettings);
        _logger.LogInformation("IsPreAwardsMonthAsync: NextMonth={NextMonth}, IsNextMonthAwards={IsAwards}",
            nextMonth, isNextMonthAwards);

        bool result = PhaseCalculator.IsMonthBeforeAwards(
            targetMonth,
            clubStartDate,
            peoplePerPhase,
            awardSettings);

        _logger.LogInformation("IsPreAwardsMonthAsync: Final result={Result}", result);
        return result;
    }

    /// <summary>
    /// Gets the next awards month date
    /// </summary>
    public async Task<DateTime?> GetNextAwardsMonthAsync()
    {
        AwardSetting? awardSettings = await _settingService.GetAwardSettingsAsync();
        if (awardSettings?.AwardsEnabled != true)
            return null;

        List<Setting> settings = await _settingService.GetAllAsync();
        Setting? startDateSetting = settings.FirstOrDefault(s => s.Key == "StartDate");

        if (startDateSetting == null || !DateTime.TryParse(startDateSetting.Value, out DateTime clubStartDate))
            return null;

        List<Person> people = await _personService.GetAllAsync();
        int peoplePerPhase = people.Count > 0 ? people.Count : 6;

        // Check next 24 months to find the next awards month
        DateTime currentMonth = DateProvider.Now.StartOfMonth();
        for (int i = 0; i < 24; i++)
        {
            DateTime checkMonth = currentMonth.AddMonths(i);
            if (PhaseCalculator.IsAwardsMonth(checkMonth, clubStartDate, peoplePerPhase, awardSettings))
            {
                return checkMonth;
            }
        }

        return null;
    }

    #endregion

    #region Event Management

    /// <summary>
    /// Gets the current category voting event (if in pre-awards month)
    /// </summary>
    public async Task<CategoryVotingEvent?> GetCurrentEventAsync()
    {
        return await _eventService.GetCurrentEventAsync();
    }

    /// <summary>
    /// Gets or creates a category voting event for the current pre-awards month.
    /// Generates categories if this is a new event.
    /// </summary>
    public async Task<CategoryVotingEvent?> GetOrCreateCurrentEventAsync()
    {
        if (!await IsPreAwardsMonthAsync())
        {
            _logger.LogInformation("Not in pre-awards month, no event to create");
            return null;
        }

        DateTime currentMonth = DateProvider.Now.StartOfMonth();

        // Get next awards month to determine target phase
        DateTime? nextAwardsMonth = await GetNextAwardsMonthAsync();
        if (!nextAwardsMonth.HasValue)
        {
            _logger.LogWarning("Could not determine next awards month");
            return null;
        }

        // Calculate target phase number
        AwardSetting? awardSettings = await _settingService.GetAwardSettingsAsync();
        List<Setting> settings = await _settingService.GetAllAsync();
        Setting? startDateSetting = settings.FirstOrDefault(s => s.Key == "StartDate");
        DateTime.TryParse(startDateSetting?.Value, out DateTime clubStartDate);

        List<Person> people = await _personService.GetAllAsync();
        int peoplePerPhase = people.Count > 0 ? people.Count : 6;

        int targetPhaseNumber = PhaseCalculator.CalculatePhaseNumber(
            nextAwardsMonth.Value,
            clubStartDate,
            peoplePerPhase,
            awardSettings);

        // Check if event already exists
        CategoryVotingEvent? existing = await _eventService.GetEventForMonthAsync(currentMonth);
        if (existing != null)
        {
            // If no categories generated yet, generate them
            if (existing.GeneratedCategories.Count == 0)
            {
                existing.GeneratedCategories = await _generationService.GenerateCategoriesAsync();
                existing.GeneratedAt = DateTime.UtcNow;
                return await _eventService.UpdateAsync(existing);
            }
            return existing;
        }

        // Create new event
        CategoryVotingEvent newEvent = await _eventService.GetOrCreateForMonthAsync(currentMonth, targetPhaseNumber);

        // Generate categories
        newEvent.GeneratedCategories = await _generationService.GenerateCategoriesAsync();
        newEvent.GeneratedAt = DateTime.UtcNow;

        return await _eventService.UpdateAsync(newEvent);
    }

    /// <summary>
    /// Regenerates categories for an existing event (admin function)
    /// </summary>
    public async Task<CategoryVotingEvent?> RegenerateCategoriesAsync(Guid eventId)
    {
        CategoryVotingEvent? evt = await _eventService.GetByIdAsync(eventId);
        if (evt == null)
        {
            _logger.LogWarning("Cannot regenerate categories - event {EventId} not found", eventId);
            return null;
        }

        if (evt.IsFinalized)
        {
            _logger.LogWarning("Cannot regenerate categories - event {EventId} is already finalized", eventId);
            return null;
        }

        evt.GeneratedCategories = await _generationService.GenerateCategoriesAsync();
        evt.GeneratedAt = DateTime.UtcNow;

        _logger.LogInformation("Regenerated categories for event {EventId}", eventId);
        return await _eventService.UpdateAsync(evt);
    }

    #endregion

    #region Voting

    /// <summary>
    /// Gets the available categories for voting
    /// </summary>
    public async Task<List<string>> GetAvailableCategoriesAsync(Guid eventId)
    {
        CategoryVotingEvent? evt = await _eventService.GetByIdAsync(eventId);
        return evt?.GeneratedCategories ?? new List<string>();
    }

    /// <summary>
    /// Checks if a user has voted
    /// </summary>
    public async Task<bool> HasVotedAsync(Guid eventId, string voterName)
    {
        return await _voteService.HasUserVotedAsync(eventId, voterName);
    }

    /// <summary>
    /// Gets a user's current vote
    /// </summary>
    public async Task<CategoryVote?> GetUserVoteAsync(Guid eventId, string voterName)
    {
        return await _voteService.GetVoteByUserAsync(eventId, voterName);
    }

    /// <summary>
    /// Casts or updates a user's vote
    /// </summary>
    public async Task<CategoryVote?> CastVoteAsync(
        Guid eventId,
        string voterName,
        string voterIp,
        Dictionary<string, int> categoryRatings)
    {
        // Validate event exists and is not finalized
        CategoryVotingEvent? evt = await _eventService.GetByIdAsync(eventId);
        if (evt == null)
        {
            _logger.LogWarning("Cannot cast vote - event {EventId} not found", eventId);
            return null;
        }

        if (evt.IsFinalized)
        {
            _logger.LogWarning("Cannot cast vote - event {EventId} is already finalized", eventId);
            return null;
        }

        // Validate all categories are rated with allowed point values
        int totalCategories = evt.GeneratedCategories.Count;
        if (categoryRatings.Count != totalCategories)
        {
            _logger.LogWarning(
                "Invalid vote - expected ratings for {Total} categories, got {Count}",
                totalCategories,
                categoryRatings.Count);
            return null;
        }

        // Validate all categories are from the generated list
        HashSet<string> validCategories = new HashSet<string>(evt.GeneratedCategories);
        foreach (KeyValuePair<string, int> rating in categoryRatings)
        {
            string category = rating.Key;
            int points = rating.Value;

            if (!validCategories.Contains(category))
            {
                _logger.LogWarning("Invalid category in vote: {Category}", category);
                return null;
            }

            if (points is < 0 or > 2)
            {
                _logger.LogWarning("Invalid rating value {Points} for category {Category}", points, category);
                return null;
            }
        }

        return await _voteService.CastVoteAsync(eventId, voterName, voterIp, categoryRatings);
    }

    /// <summary>
    /// Gets vote counts for all categories
    /// </summary>
    public async Task<List<CategoryVoteResult>> GetVoteCountsAsync(Guid eventId)
    {
        return await _voteService.GetVoteCountsAsync(eventId);
    }

    /// <summary>
    /// Gets the top 12 categories by vote count
    /// </summary>
    public async Task<List<string>> GetTop12CategoriesAsync(Guid eventId)
    {
        return await _voteService.GetTopCategoriesAsync(eventId, 12);
    }

    /// <summary>
    /// Gets all votes for an event
    /// </summary>
    public async Task<List<CategoryVote>> GetAllVotesAsync(Guid eventId)
    {
        return await _voteService.GetVotesForEventAsync(eventId);
    }

    #endregion

    #region Identity Management

    /// <summary>
    /// Gets the identity for an IP address
    /// </summary>
    public async Task<VoterIdentity?> GetIdentityByIpAsync(string ipAddress)
    {
        return await _identityService.GetIdentityByIpAsync(ipAddress);
    }

    /// <summary>
    /// Locks an IP address to a person's identity
    /// </summary>
    public async Task<VoterIdentity?> LockIdentityAsync(string ipAddress, string personName)
    {
        return await _identityService.LockIdentityAsync(ipAddress, personName);
    }

    /// <summary>
    /// Checks if an IP is already locked to a specific person
    /// </summary>
    public async Task<bool> IsIpLockedToPersonAsync(string ipAddress, string personName)
    {
        return await _identityService.IsIpLockedToPersonAsync(ipAddress, personName);
    }

    #endregion

    #region Finalization

    /// <summary>
    /// Finalizes voting and sets the top 12 categories as final
    /// </summary>
    public async Task<CategoryVotingEvent?> FinalizeVotingAsync(Guid eventId)
    {
        CategoryVotingEvent? evt = await _eventService.GetByIdAsync(eventId);
        if (evt == null)
        {
            _logger.LogWarning("Cannot finalize - event {EventId} not found", eventId);
            return null;
        }

        if (evt.IsFinalized)
        {
            _logger.LogInformation("Event {EventId} is already finalized", eventId);
            return evt;
        }

        // Get top 12 categories
        List<string> top12 = await GetTop12CategoriesAsync(eventId);

        if (top12.Count < 12)
        {
            _logger.LogWarning(
                "Not enough votes to determine top 12 categories (got {Count})",
                top12.Count);
            // Pad with remaining categories from generated list
            HashSet<string> selectedSet = new HashSet<string>(top12);
            foreach (string category in evt.GeneratedCategories)
            {
                if (!selectedSet.Contains(category))
                {
                    top12.Add(category);
                    if (top12.Count >= 12)
                        break;
                }
            }
        }

        return await _eventService.FinalizeAsync(eventId, top12);
    }

    /// <summary>
    /// Checks if all members have voted with complete ratings
    /// </summary>
    public async Task<bool> HaveAllMembersVotedAsync(Guid eventId)
    {
        List<Person> allPeople = await _personService.GetAllAsync();
        List<CategoryVote> votes = await _voteService.GetVotesForEventAsync(eventId);

        HashSet<string> voterNames = new HashSet<string>(
            votes.Select(v => v.VoterName),
            StringComparer.OrdinalIgnoreCase);

        CategoryVotingEvent? evt = await _eventService.GetByIdAsync(eventId);
        int totalCategories = evt?.GeneratedCategories.Count ?? 0;

        return allPeople.All(p => voterNames.Contains(p.Name))
               && votes.All(v => v.CategoryRatings.Count == totalCategories);
    }

    /// <summary>
    /// Checks if voting is complete for an event
    /// </summary>
    public async Task<bool> IsVotingCompleteAsync(Guid eventId)
    {
        return await HaveAllMembersVotedAsync(eventId);
    }

    /// <summary>
    /// Gets the list of people who haven't voted yet
    /// </summary>
    public async Task<List<Person>> GetPendingVotersAsync(Guid eventId)
    {
        List<Person> allPeople = await _personService.GetAllAsync();
        List<CategoryVote> votes = await _voteService.GetVotesForEventAsync(eventId);
        CategoryVotingEvent? evt = await _eventService.GetByIdAsync(eventId);
        int totalCategories = evt?.GeneratedCategories.Count ?? 0;

        HashSet<string> voterNames = new HashSet<string>(
            votes.Select(v => v.VoterName),
            StringComparer.OrdinalIgnoreCase);

        HashSet<string> incompleteVoters = new HashSet<string>(
            votes.Where(v => v.CategoryRatings.Count != totalCategories)
                .Select(v => v.VoterName),
            StringComparer.OrdinalIgnoreCase);

        return allPeople
            .Where(p => !voterNames.Contains(p.Name) || incompleteVoters.Contains(p.Name))
            .ToList();
    }

    #endregion
}
