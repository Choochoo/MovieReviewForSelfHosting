using MovieReviewApp.Application.Models.OpenAI;
using MovieReviewApp.Infrastructure.Configuration;
using MovieReviewApp.Models;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MovieReviewApp.Application.Services.Analysis;

/// <summary>
/// Service responsible for interacting with OpenAI API for transcript analysis.
/// </summary>
public class OpenAIAnalysisService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIAnalysisService> _logger;
    private readonly string _apiKey;
    private const int MAX_TRANSCRIPT_SIZE = 60000; // Balanced limit for longer timeout window
    private const string TRUNCATION_WARNING = "\n\n[TRANSCRIPT TRUNCATED DUE TO LENGTH - ANALYSIS BASED ON FIRST {0:N0} CHARACTERS FOR OPTIMAL PROCESSING]";

    public OpenAIAnalysisService(HttpClient httpClient, SecretsManager secretsManager, ILogger<OpenAIAnalysisService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // Get API key from secrets manager
        _apiKey = secretsManager.GetSecret("OpenAI:ApiKey") ?? string.Empty;

        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            _logger.LogInformation("OpenAI service initialized with API key");
        }
        else
        {
            _logger.LogWarning("OpenAI service initialized without API key - analysis will be disabled");
        }
    }

    /// <summary>
    /// Gets a value indicating whether the service is properly configured with an API key.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    /// <summary>
    /// Calls OpenAI API to analyze transcript and extract entertainment highlights.
    /// </summary>
    public async Task<string> AnalyzeTranscriptAsync(string analysisPrompt)
    {
        return await CallOpenAIForAnalysisWithRetry(analysisPrompt);
    }


    /// <summary>
    /// Calls OpenAI API with retry logic for reliability.
    /// </summary>
    private async Task<string> CallOpenAIForAnalysisWithRetry(string prompt, int maxRetries = 3)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.LogDebug("OpenAI API attempt {Attempt} of {MaxRetries}", attempt, maxRetries);

                object requestBody = new
                {
                    model = "gpt-4-turbo-preview",
                    messages = new[]
                    {
                        new { role = "system", content = "You are an expert at analyzing movie discussion transcripts to find the most entertaining and memorable moments. You have a great sense of humor and can identify what makes conversations funny, awkward, or interesting. You must respond in valid JSON format." },
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.7,
                    max_tokens = 4000,
                    response_format = new { type = "json_object" }
                };

                using StringContent jsonContent = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json");

                // Use longer timeout for OpenAI
                using HttpResponseMessage response = await _httpClient.PostAsync(
                    "https://api.openai.com/v1/chat/completions",
                    jsonContent);

                if (response.IsSuccessStatusCode)
                {
                    string responseContent = await response.Content.ReadAsStringAsync();
                    _logger.LogDebug("OpenAI API call successful on attempt {Attempt}", attempt);
                    return responseContent;
                }

                string errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("OpenAI API returned {StatusCode} on attempt {Attempt}: {Error}",
                    response.StatusCode, attempt, errorContent);

                if (attempt < maxRetries)
                {
                    int delayMs = attempt * 2000; // Exponential backoff
                    await Task.Delay(delayMs);
                }
                else
                {
                    throw new Exception($"OpenAI API failed after {maxRetries} attempts. Last error: {errorContent}");
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("OpenAI API timeout on attempt {Attempt}", attempt);
                if (attempt == maxRetries)
                {
                    throw new Exception($"OpenAI API timed out after {maxRetries} attempts");
                }
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.LogWarning(ex, "Error calling OpenAI API on attempt {Attempt}", attempt);
                await Task.Delay(attempt * 2000);
            }
        }

        throw new Exception("Failed to call OpenAI API");
    }

    /// <summary>
    /// Loads the prompt template for OpenAI analysis.
    /// </summary>
    private string LoadPromptTemplate()
    {
        // In a real implementation, this could load from a file or database
        // For now, return the hardcoded template
        return @"Analyze this movie discussion transcript to find the most entertaining, memorable, and discussion-worthy moments.

IMPORTANT RULES:
1. Use ONLY the timestamps from the master recording for all categories
2. Match quotes EXACTLY as spoken - preserve the original wording, pauses, and speech patterns
3. Include relevant context/setup before each moment
4. Focus on genuine entertainment value, not forced humor
5. Ensure variety - avoid featuring the same person too many times
6. Respond with a valid JSON object using the provided schema

For each category, find the absolute best example that matches the criteria. If a category doesn't have a good match, you can leave it with minimal/placeholder values.";
    }

    /// <summary>
    /// Creates the JSON schema for OpenAI response format.
    /// </summary>
    private string CreateResponseSchema()
    {
        // Create a sample response with all expected fields
        OpenAIAnalysisResponse sampleResponse = new OpenAIAnalysisResponse
        {
            MostOffensiveTake = CreateSampleCategoryWinner(),
            HottestTake = CreateSampleCategoryWinner(),
            BiggestArgumentStarter = CreateSampleCategoryWinner(),
            BestJoke = CreateSampleCategoryWinner(),
            BestRoast = CreateSampleCategoryWinner(),
            FunniestRandomTangent = CreateSampleCategoryWinner(),
            MostPassionateDefense = CreateSampleCategoryWinner(),
            BiggestUnanimousReaction = CreateSampleCategoryWinner(),
            MostBoringStatement = CreateSampleCategoryWinner(),
            BestPlotTwistRevelation = CreateSampleCategoryWinner(),
            MovieSnobMoment = CreateSampleCategoryWinner(),
            GuiltyPleasureAdmission = CreateSampleCategoryWinner(),
            QuietestPersonBestMoment = CreateSampleCategoryWinner(),
            Top5FunniestSentences = CreateSampleTopFiveList(),
            Top5MostBlandComments = CreateSampleTopFiveList(),
            OpeningQuestions = CreateSampleInitialQuestions()
        };

        // Serialize with indentation
        JsonSerializerOptions options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        return JsonSerializer.Serialize(sampleResponse, options);
    }

    private CategoryWinnerDto CreateSampleCategoryWinner()
    {
        return new CategoryWinnerDto
        {
            Speaker = "[Exact participant name]",
            Timestamp = "[MM:SS format]",
            Quote = "[Exact quote verified from individual mic]",
            Setup = "[Context leading to this moment]",
            GroupReaction = "[How others reacted]",
            WhyItsGreat = "[Why this is entertaining]",
            AudioQualityString = "Clear",
            EntertainmentScore = 8,
            RunnersUp = new List<RunnerUpDto>
            {
                new RunnerUpDto
                {
                    Speaker = "[Name]",
                    Timestamp = "[MM:SS]",
                    BriefDescription = "[Short description]",
                    Place = 2
                }
            }
        };
    }

    private TopFiveListDto CreateSampleTopFiveList()
    {
        return new TopFiveListDto
        {
            Entries = new List<TopFiveEntryDto>
            {
                new TopFiveEntryDto
                {
                    Rank = 1,
                    Speaker = "[Name]",
                    Timestamp = "[MM:SS]",
                    Quote = "[Quote]",
                    Context = "[Context]",
                    AudioQualityString = "Clear",
                    Score = 9.5,
                    Reasoning = "[Why this is funny/bland]"
                }
            }
        };
    }

    private InitialQuestionsDto CreateSampleInitialQuestions()
    {
        return new InitialQuestionsDto
        {
            Questions = new List<QuestionAnswerDto>
            {
                new QuestionAnswerDto
                {
                    Question = "[Question asked]",
                    Speaker = "[Who answered]",
                    Answer = "[Their answer]",
                    Timestamp = "[MM:SS]",
                    EntertainmentValue = 7
                }
            }
        };
    }
}