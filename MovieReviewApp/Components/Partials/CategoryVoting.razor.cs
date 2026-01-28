using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MovieReviewApp.Application.Services;
using MovieReviewApp.Models;

namespace MovieReviewApp.Components.Partials;

/// <summary>
/// Component for handling pre-awards category voting functionality.
/// Allows members to vote on AI-generated award categories.
/// </summary>
public partial class CategoryVoting
{
    [Parameter]
    public DateTime CurrentDate { get; set; }

    [Inject]
    private CategoryVotingService CategoryVotingService { get; set; } = default!;

    [Inject]
    private PersonService PersonService { get; set; } = default!;

    [Inject]
    private IHttpContextAccessor HttpContextAccessor { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    // State
    private bool IsLoading { get; set; } = true;
    private bool IsSubmitting { get; set; } = false;
    private CategoryVotingEvent? VotingEvent { get; set; }
    private List<Person> AllVoters { get; set; } = new();
    private List<Person> PendingVoters { get; set; } = new();
    private List<CategoryVoteResult> VoteResults { get; set; } = new();
    private bool IsVotingComplete { get; set; } = false;
    private bool HasAttemptedSubmit { get; set; } = false;

    // User state
    private string? SelectedUser { get; set; }
    private string? LockedIdentityName { get; set; }
    private bool HasVoted { get; set; }
    private Dictionary<string, int> UserCategoryRatings { get; set; } = new();
    private Dictionary<string, int> CategoryRatings { get; set; } = new();

    // Results state
    private bool ShowResults { get; set; } = false;
    private bool IsPasswordVerified { get; set; } = false;
    private bool ShowPasswordError { get; set; } = false;
    private string EnteredPassword { get; set; } = string.Empty;
    private const string RESULTS_PASSWORD = "006514";

    // Computed properties
    private int TotalVoterCount => AllVoters.Count;
    private int CompletedVoterCount => TotalVoterCount - PendingVoters.Count;
    private int TotalCategoryCount => VotingEvent?.GeneratedCategories.Count ?? 0;
    private int RatedCount => CategoryRatings.Count;
    private bool IsVotingLocked => (VotingEvent?.IsFinalized ?? false) || IsVotingComplete;
    private List<string> UnratedCategories => VotingEvent?.GeneratedCategories
        .Where(c => !CategoryRatings.ContainsKey(c))
        .ToList() ?? new List<string>();

    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        IsLoading = true;

        try
        {
            // Load voting event
            VotingEvent = await CategoryVotingService.GetOrCreateCurrentEventAsync();

            if (VotingEvent != null)
            {
                // Load all voters
                AllVoters = await PersonService.GetAllOrderedAsync(true);

                // Load pending voters
                PendingVoters = await CategoryVotingService.GetPendingVotersAsync(VotingEvent.Id);

                // Load vote results
                VoteResults = await CategoryVotingService.GetVoteCountsAsync(VotingEvent.Id);

                // Check if voting is complete and finalize if needed
                IsVotingComplete = await CategoryVotingService.IsVotingCompleteAsync(VotingEvent.Id);
                if (IsVotingComplete)
                {
                    CategoryVotingEvent? finalized = await CategoryVotingService.FinalizeVotingAsync(VotingEvent.Id);
                    if (finalized != null)
                    {
                        VotingEvent = finalized;
                    }

                    ShowResults = true;
                    IsPasswordVerified = true;
                }

                // Check for locked identity
                string userIp = GetUserIp();
                VoterIdentity? identity = await CategoryVotingService.GetIdentityByIpAsync(userIp);
                if (identity != null)
                {
                    LockedIdentityName = identity.PersonName;
                }

                // Try to restore user from localStorage
                SelectedUser = await GetStoredUser();

                // If user is logged in, check if they've voted
                if (!string.IsNullOrEmpty(SelectedUser))
                {
                    await LoadUserVoteStatus();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading category voting data: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadUserVoteStatus()
    {
        if (VotingEvent == null || string.IsNullOrEmpty(SelectedUser))
            return;

        CategoryVote? existingVote = await CategoryVotingService.GetUserVoteAsync(VotingEvent.Id, SelectedUser);

        if (existingVote != null)
        {
            HasVoted = existingVote.CategoryRatings.Count > 0;
            UserCategoryRatings = new Dictionary<string, int>(existingVote.CategoryRatings);
            CategoryRatings = new Dictionary<string, int>(existingVote.CategoryRatings);
        }
        else
        {
            HasVoted = false;
            UserCategoryRatings = new Dictionary<string, int>();
            CategoryRatings = new Dictionary<string, int>();
        }
    }

    private string GetUserIp()
    {
        return HttpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private async Task<string> GetStoredUser()
    {
        try
        {
            return await JS.InvokeAsync<string>("localStorage.getItem", "categoryVoterName") ?? "";
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
            await JS.InvokeVoidAsync("localStorage.setItem", "categoryVoterName", username);
        }
        catch
        {
            // Handle storage errors
        }
    }

    private async Task SelectIdentity(string personName)
    {
        string userIp = GetUserIp();

        // Try to lock the identity
        VoterIdentity? identity = await CategoryVotingService.LockIdentityAsync(userIp, personName);

        if (identity == null)
        {
            // IP was already locked to a different person
            await JS.InvokeVoidAsync("alert", $"This device is already registered to {LockedIdentityName}. Please use that identity or contact an admin.");
            return;
        }

        SelectedUser = personName;
        LockedIdentityName = personName;
        await StoreUser(personName);
        await LoadUserVoteStatus();
        StateHasChanged();
    }

    private async Task UseLockedIdentity()
    {
        if (!string.IsNullOrEmpty(LockedIdentityName))
        {
            SelectedUser = LockedIdentityName;
            await StoreUser(LockedIdentityName);
            await LoadUserVoteStatus();
            StateHasChanged();
        }
    }

    private async Task LogOut()
    {
        SelectedUser = null;
        HasVoted = false;
        UserCategoryRatings = new Dictionary<string, int>();
        CategoryRatings = new Dictionary<string, int>();
        HasAttemptedSubmit = false;

        try
        {
            await JS.InvokeVoidAsync("localStorage.removeItem", "categoryVoterName");
        }
        catch
        {
            // Handle storage errors
        }

        StateHasChanged();
    }

    private int GetRatingForCategory(string category)
    {
        return CategoryRatings.TryGetValue(category, out int rating) ? rating : -1;
    }

    private void SetRating(string category, int rating)
    {
        if (IsVotingLocked)
            return;

        if (rating < 0)
        {
            CategoryRatings.Remove(category);
        }
        else
        {
            CategoryRatings[category] = rating;
        }

        StateHasChanged();
    }

    private void AttemptSubmit()
    {
        HasAttemptedSubmit = true;
        StateHasChanged();

        if (UnratedCategories.Any())
        {
            _ = ScrollToCategory(UnratedCategories.First());
        }
    }

    private async Task ScrollToCategory(string category)
    {
        string elementId = $"category-{category.GetHashCode()}";
        await JS.InvokeVoidAsync("scrollToElement", elementId);
    }

    private async Task SubmitVotes()
    {
        if (VotingEvent == null || string.IsNullOrEmpty(SelectedUser))
            return;

        HasAttemptedSubmit = true;

        if (UnratedCategories.Any())
        {
            await JS.InvokeVoidAsync("alert", $"Please rate all categories. You have {UnratedCategories.Count} unrated.");
            return;
        }

        IsSubmitting = true;
        StateHasChanged();

        try
        {
            string userIp = GetUserIp();
            CategoryVote? vote = await CategoryVotingService.CastVoteAsync(
                VotingEvent.Id,
                SelectedUser,
                userIp,
                new Dictionary<string, int>(CategoryRatings));

            if (vote != null)
            {
                HasVoted = true;
                UserCategoryRatings = new Dictionary<string, int>(vote.CategoryRatings);

                // Refresh data
                PendingVoters = await CategoryVotingService.GetPendingVotersAsync(VotingEvent.Id);
                VoteResults = await CategoryVotingService.GetVoteCountsAsync(VotingEvent.Id);
                IsVotingComplete = await CategoryVotingService.IsVotingCompleteAsync(VotingEvent.Id);

                if (IsVotingComplete)
                {
                    CategoryVotingEvent? finalized = await CategoryVotingService.FinalizeVotingAsync(VotingEvent.Id);
                    if (finalized != null)
                    {
                        VotingEvent = finalized;
                    }

                    ShowResults = true;
                    IsPasswordVerified = true;
                }

                await JS.InvokeVoidAsync("alert", "Your votes have been submitted successfully!");
            }
            else
            {
                await JS.InvokeVoidAsync("alert", "Failed to submit votes. Please try again.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error submitting votes: {ex.Message}");
            await JS.InvokeVoidAsync("alert", "An error occurred while submitting your votes.");
        }
        finally
        {
            IsSubmitting = false;
            StateHasChanged();
        }
    }

    private void ToggleResults()
    {
        if (IsVotingComplete)
        {
            ShowResults = true;
            IsPasswordVerified = true;
            StateHasChanged();
            return;
        }

        ShowResults = !ShowResults;

        if (!ShowResults)
        {
            IsPasswordVerified = false;
            ShowPasswordError = false;
            EnteredPassword = "";
        }

        StateHasChanged();
    }

    private void VerifyPassword()
    {
        if (EnteredPassword == RESULTS_PASSWORD)
        {
            IsPasswordVerified = true;
            ShowPasswordError = false;
        }
        else
        {
            ShowPasswordError = true;
        }

        EnteredPassword = "";
        StateHasChanged();
    }

    private static string GetRatingLabel(int rating)
    {
        return rating switch
        {
            0 => "Don't Like",
            1 => "Like",
            2 => "Love",
            _ => "Not Selected"
        };
    }

    private static string GetRatingBadgeClass(int rating)
    {
        return rating switch
        {
            0 => "bg-danger",
            1 => "bg-warning text-dark",
            2 => "bg-success",
            _ => "bg-secondary"
        };
    }
}
