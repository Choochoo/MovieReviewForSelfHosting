using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using MovieReviewApp.Infrastructure.Configuration;
using Newtonsoft.Json.Linq;

namespace MovieReviewApp.Application.Services;

public class TmdbService
{
    private readonly HttpClient _httpClient;
    private readonly SecretsManager _secretsManager;
    private readonly string? _apiKey;
    private const string BaseUrl = "https://api.themoviedb.org/3";
    private const string ImageBaseUrl = "https://image.tmdb.org/t/p/w500";

    public TmdbService(HttpClient httpClient, SecretsManager secretsManager)
    {
        _httpClient = httpClient;
        _secretsManager = secretsManager;
        _apiKey = _secretsManager.GetSecret("TMDB:ApiKey");
    }

    public async Task<TmdbMovieInfo?> GetMovieInfoAsync(string movieTitle)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            Console.WriteLine("TMDB API key not configured");
            return null;
        }

        try
        {
            // Search for the movie with language preference
            string encodedTitle = Uri.EscapeDataString(movieTitle);
            string searchUrl = $"{BaseUrl}/search/movie?api_key={_apiKey}&query={encodedTitle}&language=en-US&include_adult=false";

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            request.Headers.Add("User-Agent", "MovieReviewApp/1.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using HttpResponseMessage response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"TMDB search failed for '{movieTitle}': {response.StatusCode}");
                return null;
            }

            string responseBody = await response.Content.ReadAsStringAsync();
            JObject searchResult = JObject.Parse(responseBody);

            if (searchResult["results"] == null || !searchResult["results"]!.HasValues)
            {
                Console.WriteLine($"No TMDB results found for '{movieTitle}'");
                return null;
            }

            // Find best match - prioritize exact title matches and English language
            JToken? bestMatch = null;
            foreach (JToken result in searchResult["results"]!)
            {
                string? title = result["title"]?.ToString();
                string? originalTitle = result["original_title"]?.ToString();
                string? language = result["original_language"]?.ToString();

                // Exact match (case insensitive)
                if (string.Equals(title, movieTitle, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(originalTitle, movieTitle, StringComparison.OrdinalIgnoreCase))
                {
                    bestMatch = result;
                    break;
                }

                // Prefer English movies if no exact match found yet
                if (bestMatch == null && language == "en")
                {
                    bestMatch = result;
                }

                // Fall back to first result if nothing better found
                if (bestMatch == null)
                {
                    bestMatch = result;
                }
            }

            if (bestMatch == null)
            {
                Console.WriteLine($"No suitable TMDB match found for '{movieTitle}'");
                return null;
            }

            JToken? idToken = bestMatch["id"];
            if (idToken == null)
            {
                Console.WriteLine($"Invalid movie ID in TMDB response for '{movieTitle}'");
                return null;
            }

            int movieId = (int)idToken;
            string matchedTitle = bestMatch["title"]?.ToString() ?? "Unknown";
            string matchedYear = bestMatch["release_date"]?.ToString()?.Substring(0, 4) ?? "";
            Console.WriteLine($"   🎬 Matched '{movieTitle}' → '{matchedTitle}' ({matchedYear}) [ID: {movieId}]");

            // Get detailed movie info
            string detailsUrl = $"{BaseUrl}/movie/{movieId}?api_key={_apiKey}&append_to_response=external_ids";
            HttpRequestMessage detailsRequest = new HttpRequestMessage(HttpMethod.Get, detailsUrl);
            detailsRequest.Headers.Add("User-Agent", "MovieReviewApp/1.0");
            detailsRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using HttpResponseMessage detailsResponse = await _httpClient.SendAsync(detailsRequest);
            if (!detailsResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"TMDB details failed for movie ID {movieId}: {detailsResponse.StatusCode}");
                return null;
            }

            string detailsBody = await detailsResponse.Content.ReadAsStringAsync();
            JObject movieDetails = JObject.Parse(detailsBody);

            return new TmdbMovieInfo
            {
                Title = movieDetails["title"]?.ToString() ?? movieTitle,
                Synopsis = movieDetails["overview"]?.ToString() ?? "",
                PosterPath = movieDetails["poster_path"]?.ToString(),
                PosterUrl = !string.IsNullOrEmpty(movieDetails["poster_path"]?.ToString())
                    ? $"{ImageBaseUrl}{movieDetails["poster_path"]}"
                    : null,
                ImdbId = movieDetails["external_ids"]?["imdb_id"]?.ToString(),
                ImdbUrl = !string.IsNullOrEmpty(movieDetails["external_ids"]?["imdb_id"]?.ToString())
                    ? $"https://www.imdb.com/title/{movieDetails["external_ids"]!["imdb_id"]}"
                    : null,
                ReleaseDate = DateTime.TryParse(movieDetails["release_date"]?.ToString(), out DateTime releaseDate)
                    ? releaseDate
                    : null,
                TmdbId = movieId,
                OriginalTitle = movieDetails["original_title"]?.ToString() ?? movieTitle,
                Runtime = movieDetails["runtime"]?.ToObject<int?>(),
                VoteAverage = movieDetails["vote_average"]?.ToObject<double?>(),
                Genres = movieDetails["genres"]?.Select(g => g["name"]?.ToString()).OfType<string>().ToList() ?? new List<string>()
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching TMDB data for '{movieTitle}': {ex.Message}");
            return null;
        }
    }

    public class TmdbMovieInfo
    {
        public string Title { get; set; } = "";
        public string Synopsis { get; set; } = "";
        public string? PosterPath { get; set; }
        public string? PosterUrl { get; set; }
        public string? ImdbId { get; set; }
        public string? ImdbUrl { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public int TmdbId { get; set; }
        public string OriginalTitle { get; set; } = "";
        public int? Runtime { get; set; }
        public double? VoteAverage { get; set; }
        public List<string> Genres { get; set; } = new();
    }
}