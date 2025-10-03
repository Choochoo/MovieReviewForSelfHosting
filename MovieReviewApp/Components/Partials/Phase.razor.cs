using Microsoft.AspNetCore.Components;
using MovieReviewApp.Models;
using MovieReviewApp.Application.Services;
using MovieReviewApp.Extensions;
using MovieReviewApp.Infrastructure.Configuration;
using MovieReviewApp.Infrastructure.FileSystem;
using MovieReviewApp.Infrastructure.Services;
using MovieReviewApp.Utilities;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;

namespace MovieReviewApp.Components.Partials
{
    public partial class Phase
    {
        [Parameter]
        public MovieEvent? MovieEvent { get; set; }

        [Parameter]
        public EventCallback OnPersonSwapped { get; set; }

        [Inject]
        private MovieEventService MovieEventService { get; set; } = default!;

        [Inject]
        private HttpClient Http { get; set; } = default!;

        [Inject]
        private IConfiguration Configuration { get; set; } = default!;

        [Inject]
        private DemoProtectionService demoProtection { get; set; } = default!;

        [Inject]
        private SecretsManager SecretsManager { get; set; } = default!;

        [Inject]
        private ImageService ImageService { get; set; } = default!;

        [Inject]
        private MarkdownService MarkdownService { get; set; } = default!;

        [Inject]
        private PromptService PromptService { get; set; } = default!;

        [Inject]
        private PersonService PersonService { get; set; } = default!;

        [Inject]
        private PersonAssignmentCacheService PersonAssignmentCache { get; set; } = default!;

        [Inject]
        private TmdbService TmdbService { get; set; } = default!;

        private bool showMarkdownPreview = false;
        private bool isLoading = false;
        private bool isSpicing = false;
        private bool spiceItUp = false;
        private string selectedStyle = "";
        private int selectedYear = DateTime.Now.Year;

        // Month swap fields
        private List<Person>? _allPeople;
        private string? _originalPerson;
        private string? _selectedPerson;

        // Auto-download poster fields
        private bool autoDownloadPoster = false;
        private TmdbService.TmdbMovieInfo? _cachedMovieInfo;

        // Property to handle datetime-local input binding properly
        private DateTime? MeetupTimeForInput
        {
            get
            {
                if (MovieEvent?.MeetupTime == null) return null;
                // Convert to local time if it's UTC, otherwise return as-is
                return MovieEvent.MeetupTime.Value.Kind == DateTimeKind.Utc 
                    ? MovieEvent.MeetupTime.Value.ToLocalTime() 
                    : MovieEvent.MeetupTime.Value;
            }
            set
            {
                if (MovieEvent != null)
                {
                    // Ensure the DateTime is stored as local time
                    MovieEvent.MeetupTime = value.HasValue 
                        ? DateTime.SpecifyKind(value.Value, DateTimeKind.Local) 
                        : null;
                }
            }
        }
        private string? Synopsis { get; set; }
        private string _apiKey = string.Empty;
        
        // Demo notification state
        private bool showDemoNotification = false;
        private string demoNotificationMessage = "";

        /// <summary>
        /// Initializes the component and loads TMDB API configuration.
        /// </summary>
        protected override async Task OnInitializedAsync()
        {
            try
            {
                _apiKey = SecretsManager.GetSecret("TMDB:ApiKey") ?? Configuration["TMDB:ApiKey"] ?? "";
                if (string.IsNullOrEmpty(_apiKey))
                {
                    throw new Exception("TMDB API key not found in configuration");
                }

                if (MovieEvent != null && !string.IsNullOrEmpty(MovieEvent.Movie))
                {
                    await LoadSynopsisAsync();
                }

                // Load all people for swap dropdown
                _allPeople = await PersonService.GetAllAsync();
                _originalPerson = MovieEvent?.Person;
                _selectedPerson = MovieEvent?.Person;
            }
            catch (Exception ex)
            {
                Synopsis = "Error loading configuration";
                Console.WriteLine($"Initialization error: {ex.Message}");
            }
        }

        protected override async Task OnParametersSetAsync()
        {
            try
            {
                if (MovieEvent != null && MovieEvent.SeenDate.HasValue)
                {
                    selectedYear = MovieEvent.SeenDate.Value.Year;
                }
                else
                {
                    selectedYear = DateProvider.Now.Year;
                }

                if (MovieEvent != null && !string.IsNullOrEmpty(MovieEvent.Movie))
                {
                    await GetMovieSynopsisAsync(MovieEvent.Movie);
                }
            }
            catch (Exception ex)
            {
                selectedYear = DateProvider.Now.Year;
                Synopsis = "Error loading movie details";
                Console.WriteLine($"Parameter set error: {ex.Message}");
            }
        }

        private async Task LoadSynopsisAsync()
        {
            if (MovieEvent == null || string.IsNullOrEmpty(MovieEvent.Movie)) return;

            try
            {
                HttpResponseMessage response = await Http.GetAsync($"/api/movie/synopsis?title={Uri.EscapeDataString(MovieEvent.Movie)}");
                if (response.IsSuccessStatusCode)
                {
                    Synopsis = await response.Content.ReadAsStringAsync();
                }
            }
            catch
            {
                Synopsis = null;
            }
        }

        /// <summary>
        /// Fetches movie synopsis and metadata from TMDB using TmdbService and caches it.
        /// </summary>
        private async Task GetMovieSynopsisAsync(string movieTitle)
        {
            if (string.IsNullOrEmpty(movieTitle))
            {
                Synopsis = "No movie title provided";
                return;
            }

            try
            {
                // Return cached synopsis if already available
                if (MovieEvent != null && !string.IsNullOrEmpty(MovieEvent.Synopsis))
                {
                    Synopsis = MovieEvent.Synopsis;
                    return;
                }

                // Fetch movie info from TMDB using TmdbService
                _cachedMovieInfo = await TmdbService.GetMovieInfoAsync(movieTitle);

                if (_cachedMovieInfo == null)
                {
                    Synopsis = "Movie not found.";
                    return;
                }

                Synopsis = _cachedMovieInfo.Synopsis;

                // Save synopsis to database if valid
                if (MovieEvent != null && !string.IsNullOrEmpty(Synopsis) &&
                    Synopsis != "Synopsis not available." && Synopsis != "Movie not found.")
                {
                    try
                    {
                        if (!demoProtection.TryValidateNotDemo("Update movie synopsis", out string errorMessage))
                        {
                            await ShowDemoNotification(errorMessage);
                            return;
                        }

                        MovieEvent.Synopsis = Synopsis;
                        await Task.Run(() => MovieEventService.UpsertAsync(MovieEvent));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Cache error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching movie synopsis: {ex.Message}");
                Synopsis = "Error loading movie data.";
            }
        }

        private void Edit()
        {
            try
            {
                if (MovieEvent != null)
                {
                    MovieEvent.IsEditing = true;

                    if (!string.IsNullOrEmpty(MovieEvent.Reasoning))
                    {
                        MovieEvent.Reasoning = MarkdownService.RemoveLineBreaks(MovieEvent.Reasoning);
                    }

                    if (!MovieEvent.MeetupTime.HasValue)
                    {
                        MovieEvent.MeetupTime = DateTime.SpecifyKind(MovieEvent.EndDate.Date.AddHours(18), DateTimeKind.Local);
                    }

                    // Reset swap tracking when entering edit mode
                    _originalPerson = MovieEvent.Person;
                    _selectedPerson = MovieEvent.Person;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Edit error: {ex.Message}");
            }
        }

        private void Cancel()
        {
            try
            {
                if (MovieEvent != null)
                    MovieEvent.IsEditing = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Cancel error: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves the movie event with optional text spicing and image processing.
        /// </summary>
        private async Task SaveAsync()
        {
            if (isLoading) return;

            try
            {
                isLoading = true;
                if (MovieEvent != null)
                {
                    if (spiceItUp && !string.IsNullOrEmpty(selectedStyle) && !string.IsNullOrEmpty(MovieEvent.Reasoning))
                    {
                        isSpicing = true;
                        StateHasChanged();

                        string spicedText = await PromptService.SpiceUpTextAsync(MovieEvent.Reasoning, selectedStyle);
                        if (!string.IsNullOrEmpty(spicedText))
                        {
                            MovieEvent.Reasoning = spicedText;
                        }

                        isSpicing = false;
                    }

                    // Auto-download poster from TMDB if checkbox is checked
                    if (autoDownloadPoster && !MovieEvent.ImageId.HasValue && !string.IsNullOrEmpty(MovieEvent.Movie))
                    {
                        try
                        {
                            // Re-fetch if movie title changed since synopsis load
                            if (_cachedMovieInfo?.Title != MovieEvent.Movie)
                            {
                                _cachedMovieInfo = await TmdbService.GetMovieInfoAsync(MovieEvent.Movie);
                            }

                            // Download poster directly to ImageId (never set PosterUrl)
                            if (_cachedMovieInfo?.PosterUrl != null)
                            {
                                Guid? imageId = await ImageService.SaveImageFromUrlAsync(_cachedMovieInfo.PosterUrl);
                                if (imageId.HasValue)
                                {
                                    MovieEvent.ImageId = imageId;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Auto-download poster failed: {ex.Message}");
                            // Continue save - graceful degradation
                        }
                    }

                    if (!string.IsNullOrEmpty(MovieEvent.PosterUrl) && !MovieEvent.ImageId.HasValue)
                    {
                        Guid? imageId = await ImageService.SaveImageFromUrlAsync(MovieEvent.PosterUrl);
                        if (imageId.HasValue)
                        {
                            MovieEvent.ImageId = imageId;
                            MovieEvent.PosterUrl = null;
                        }
                    }

                    if (MovieEvent.AlreadySeen)
                    {
                        MovieEvent.SeenDate = new DateTime(selectedYear, 1, 1);
                    }

                    if (!string.IsNullOrEmpty(MovieEvent.Reasoning))
                    {
                        MovieEvent.Reasoning = MarkdownService.AddLineBreaks(MovieEvent.Reasoning);
                    }

                    if (!demoProtection.TryValidateNotDemo("Save movie changes", out string saveErrorMessage))
                    {
                        await ShowDemoNotification(saveErrorMessage);
                        return;
                    }

                    // Handle person swap if person was changed
                    if (_selectedPerson != _originalPerson && !string.IsNullOrEmpty(_selectedPerson))
                    {
                        // CRITICAL: Save ALL user edits BEFORE swap to prevent data loss
                        // Swap logic reloads from DB which would overwrite unsaved changes (Movie, Reasoning, IMDb, ImageId)
                        await Task.Run(() => MovieEventService.UpsertAsync(MovieEvent));

                        // Find where the new person is currently assigned
                        DateTime? targetMonth = await FindMonthForPersonAsync(_selectedPerson);

                        if (targetMonth.HasValue)
                        {
                            // Perform swap (handles both MovieEvent creates/updates)
                            await MovieEventService.SwapMonthAssignmentsAsync(MovieEvent.StartDate, targetMonth.Value);

                            // Reload the event to get updated data
                            MovieEvent? updatedEvent = await MovieEventService.GetByIdAsync(MovieEvent.Id);
                            if (updatedEvent != null)
                            {
                                MovieEvent = updatedEvent;
                                MovieEvent.IsEditing = false;
                                _originalPerson = MovieEvent.Person;
                                _selectedPerson = MovieEvent.Person;
                            }
                            else
                            {
                                MovieEvent.IsEditing = false;
                            }

                            StateHasChanged();

                            // Notify parent component to refresh timeline
                            await OnPersonSwapped.InvokeAsync();

                            return; // Exit early - swap already handled save
                        }
                    }

                    await Task.Run(() => MovieEventService.UpsertAsync(MovieEvent));

                    MovieEvent? updatedMovieEvent = await MovieEventService.GetByIdAsync(MovieEvent.Id);
                    if (updatedMovieEvent != null)
                    {
                        MovieEvent = updatedMovieEvent;
                        MovieEvent.IsEditing = false;
                        StateHasChanged();
                    }

                    if (MovieEvent != null)
                    {
                        MovieEvent.IsEditing = false;
                    }

                    StateHasChanged();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Save error: {ex.Message}");
            }
            finally
            {
                isLoading = false;
            }
        }
        
        private async Task ShowDemoNotification(string message)
        {
            demoNotificationMessage = message;
            showDemoNotification = true;
            StateHasChanged();
            await Task.Delay(100); // Small delay to ensure UI updates
        }
        
        private async Task HideDemoNotification()
        {
            showDemoNotification = false;
            StateHasChanged();
        }

        /// <summary>
        /// Finds the month where the specified person is currently assigned.
        /// Database overrides cache (source of truth for swapped assignments).
        /// </summary>
        private async Task<DateTime?> FindMonthForPersonAsync(string personName)
        {
            if (string.IsNullOrEmpty(personName) || MovieEvent == null)
                return null;

            DateTime currentMonth = MovieEvent.StartDate.StartOfMonth();
            DateTime now = DateTime.Now.StartOfMonth();

            // Get all future database events (source of truth for swaps)
            List<MovieEvent> allEvents = await MovieEventService.GetAllAsync();

            // Check database first
            MovieEvent? dbMatch = allEvents
                .Where(e => e.Person == personName &&
                           e.StartDate >= now &&
                           e.StartDate.StartOfMonth() != currentMonth)
                .OrderBy(e => e.StartDate)
                .FirstOrDefault();

            if (dbMatch != null)
                return dbMatch.StartDate.StartOfMonth();

            // Fall back to cache for months without database records
            IReadOnlyDictionary<DateTime, string> cacheAssignments = await PersonAssignmentCache.GetAllAssignmentsAsync();

            foreach (KeyValuePair<DateTime, string> assignment in cacheAssignments)
            {
                // Skip current month, awards, and past months
                if (assignment.Key == currentMonth ||
                    assignment.Value.StartsWith("Awards Event") ||
                    assignment.Key < now)
                    continue;

                // Skip if database has a record for this month (already checked above)
                if (allEvents.Any(e => e.StartDate.StartOfMonth() == assignment.Key))
                    continue;

                if (assignment.Value == personName)
                    return assignment.Key;
            }

            return null;
        }
    }
}