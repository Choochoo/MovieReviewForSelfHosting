using Microsoft.AspNetCore.Components;
using MovieReviewApp.Models;
using MovieReviewApp.Application.Services;
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

        [Inject]
        private MovieEventService MovieEventService { get; set; } = default!;

        [Inject]
        private HttpClient Http { get; set; } = default!;

        [Inject]
        private IConfiguration Configuration { get; set; } = default!;

        [Inject]
        private MessengerService MessengerService { get; set; } = default!;

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

        private bool showMarkdownPreview = false;
        private bool isLoading = false;
        private bool isSpicing = false;
        private bool spiceItUp = false;
        private string selectedStyle = "";
        private int selectedYear = DateTime.Now.Year;
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
        /// Fetches movie synopsis from TMDB API and caches it.
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
                if (string.IsNullOrEmpty(_apiKey))
                {
                    Console.WriteLine("API Key is empty or null");
                    Synopsis = "Configuration error: API key not found";
                    return;
                }

                if (MovieEvent != null && !string.IsNullOrEmpty(MovieEvent.Synopsis))
                {
                    Synopsis = MovieEvent.Synopsis;
                    return;
                }

                string encodedTitle = Uri.EscapeDataString(movieTitle);
                string searchUrl = $"https://api.themoviedb.org/3/search/movie?api_key={_apiKey}&query={encodedTitle}";

                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                request.Headers.Add("User-Agent", "MovieReviewApp/1.0");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using HttpResponseMessage response = await Http.SendAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error Response Body: {responseBody}");
                    Synopsis = $"API Error: {response.StatusCode} - {responseBody}";
                    return;
                }

                JObject searchResult = JObject.Parse(responseBody);

                if (searchResult["results"] != null && searchResult["results"].HasValues)
                {
                    int movieId = (int)searchResult["results"][0]["id"];
                    string detailsUrl = $"https://api.themoviedb.org/3/movie/{movieId}?api_key={_apiKey}";

                    HttpRequestMessage detailsRequest = new HttpRequestMessage(HttpMethod.Get, detailsUrl);
                    detailsRequest.Headers.Add("User-Agent", "MovieReviewApp/1.0");
                    detailsRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    using HttpResponseMessage detailsResponse = await Http.SendAsync(detailsRequest);
                    string detailsBody = await detailsResponse.Content.ReadAsStringAsync();

                    if (!detailsResponse.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Details Error Response: {detailsBody}");
                        Synopsis = $"Movie Details Error: {detailsResponse.StatusCode}";
                        return;
                    }

                    JObject movieDetails = JObject.Parse(detailsBody);
                    Synopsis = movieDetails["overview"]?.ToString() ?? "Synopsis not available.";

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
                else
                {
                    Synopsis = "Movie not found.";
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"HTTP Request Exception: {ex.Message}");
                Synopsis = $"Network Error: {ex.Message}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"General Exception: {ex.Message}");
                Synopsis = $"Error: {ex.Message}";
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
                        MovieEvent.MeetupTime = MovieEvent.EndDate.Date.AddHours(18);
                    }
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
                    
                    await Task.Run(() => MovieEventService.UpsertAsync(MovieEvent));

                    MovieEvent? updatedMovieEvent = await MovieEventService.GetByIdAsync(MovieEvent.Id);
                    if (updatedMovieEvent != null)
                    {
                        MovieEvent = updatedMovieEvent;
                        MovieEvent.IsEditing = false;
                        StateHasChanged();
                    }

                    if (!string.IsNullOrEmpty(MovieEvent.Movie))
                    {
                        //await MessengerService.SendMovieUpdateMessage(MovieEvent.Person, MovieEvent.Movie);
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
    }
} 