using MovieReviewApp.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MovieReviewApp.Application.Services.Analysis;

/// <summary>
/// Service responsible for parsing OpenAI analysis responses into structured CategoryResults objects.
/// Handles multiple response formats and provides fallback parsing strategies.
/// </summary>
public class ResponseParsingService
{
    private readonly ILogger<ResponseParsingService> _logger;

    public ResponseParsingService(ILogger<ResponseParsingService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses an OpenAI analysis response string into CategoryResults object.
    /// </summary>
    public CategoryResults ParseAnalysisResult(string analysisResult)
    {
        if (string.IsNullOrWhiteSpace(analysisResult))
        {
            _logger.LogWarning("Received empty analysis result");
            return new CategoryResults();
        }

        _logger.LogDebug("Parsing analysis result: {Length} characters", analysisResult.Length);

        // Remove any markdown formatting
        string cleanedResult = analysisResult.Replace("```json", "").Replace("```", "").Trim();
        
        // Log first 500 characters of the cleaned result for debugging
        _logger.LogDebug("Cleaned result preview: {Preview}", 
            cleanedResult.Length > 500 ? cleanedResult.Substring(0, 500) + "..." : cleanedResult);

        // Try different parsing strategies
        CategoryResults? results = TryParseNestedStructure(cleanedResult) ?? TryParseFlatStructure(cleanedResult);

        if (results == null)
        {
            _logger.LogWarning("Failed to parse analysis result, using fallback structure");
            _logger.LogDebug("Full analysis result that failed to parse: {Result}", cleanedResult);
            return CreateFallbackResults();
        }

        _logger.LogInformation("Successfully parsed analysis result with {Count} categories", GetParsedCategoriesCount(results));
        return results;
    }

    /// <summary>
    /// Attempts to parse a nested JSON structure where categories are grouped.
    /// </summary>
    private CategoryResults? TryParseNestedStructure(string jsonContent)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(jsonContent);
            JsonElement root = doc.RootElement;

            CategoryResults results = new CategoryResults();

            // Parse each category section
            if (TryGetNestedCategory(root, "comedy_categories", "best_joke", out JsonElement bestJoke))
                results.BestJoke = ParseCategoryWinner(bestJoke);
            
            if (TryGetNestedCategory(root, "comedy_categories", "most_offensive_take", out JsonElement offensiveTake))
                results.MostOffensiveTake = ParseCategoryWinner(offensiveTake);

            if (TryGetNestedCategory(root, "opinion_categories", "hottest_take", out JsonElement hottestTake))
                results.HottestTake = ParseCategoryWinner(hottestTake);

            if (TryGetNestedCategory(root, "insight_categories", "best_plot_twist", out JsonElement plotTwist))
                results.BestPlotTwistRevelation = ParseCategoryWinner(plotTwist);

            if (TryGetNestedCategory(root, "discussion_categories", "biggest_argument_starter", out JsonElement argumentStarter))
                results.BiggestArgumentStarter = ParseCategoryWinner(argumentStarter);

            // Parse Top 5 lists
            if (TryGetNestedCategory(root, "top_5_lists", "funniest_sentences", out JsonElement funniestSentences))
                results.FunniestSentences = ParseTopFiveList(funniestSentences, "Funniest Sentences");

            if (TryGetNestedCategory(root, "top_5_lists", "most_bland_comments", out JsonElement blandComments))
                results.MostBlandComments = ParseTopFiveList(blandComments, "Most Bland Comments");

            return results;
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse nested structure");
            return null;
        }
    }

    /// <summary>
    /// Attempts to parse a flat JSON structure where categories are at the root level.
    /// </summary>
    private CategoryResults? TryParseFlatStructure(string jsonContent)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(jsonContent);
            JsonElement root = doc.RootElement;

            CategoryResults results = new CategoryResults();

            // Parse each property based on the actual OpenAI response format
            foreach (JsonProperty property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "BestJoke":
                        results.BestJoke = ParseCategoryWinner(property.Value);
                        break;
                    case "HottestTake":
                        results.HottestTake = ParseCategoryWinner(property.Value);
                        break;
                    case "MostOffensiveTake":
                        results.MostOffensiveTake = ParseCategoryWinner(property.Value);
                        break;
                    case "BiggestArgumentStarter":
                        results.BiggestArgumentStarter = ParseCategoryWinner(property.Value);
                        break;
                    case "BestRoast":
                        results.BestRoast = ParseCategoryWinner(property.Value);
                        break;
                    case "FunniestRandomTangent":
                        results.FunniestRandomTangent = ParseCategoryWinner(property.Value);
                        break;
                    case "MostPassionateDefense":
                        results.MostPassionateDefense = ParseCategoryWinner(property.Value);
                        break;
                    case "BiggestUnanimousReaction":
                        results.BiggestUnanimousReaction = ParseCategoryWinner(property.Value);
                        break;
                    case "MostBoringStatement":
                        results.MostBoringStatement = ParseCategoryWinner(property.Value);
                        break;
                    case "BestPlotTwistRevelation":
                        results.BestPlotTwistRevelation = ParseCategoryWinner(property.Value);
                        break;
                    case "MovieSnobMoment":
                        results.MovieSnobMoment = ParseCategoryWinner(property.Value);
                        break;
                    case "GuiltyPleasureAdmission":
                        results.GuiltyPleasureAdmission = ParseCategoryWinner(property.Value);
                        break;
                    case "QuietestPersonBestMoment":
                        results.QuietestPersonBestMoment = ParseCategoryWinner(property.Value);
                        break;
                    case "Top5FunniestSentences":
                        results.FunniestSentences = ParseTopFiveList(property.Value, "Funniest Sentences");
                        break;
                    case "Top5MostBlandComments":
                        results.MostBlandComments = ParseTopFiveList(property.Value, "Most Bland Comments");
                        break;
                    case "OpeningQuestions":
                        results.InitialQuestions = ParseOpeningQuestions(property.Value);
                        break;
                    default:
                        _logger.LogDebug("Unrecognized category in response: {CategoryName}", property.Name);
                        break;
                }
            }

            _logger.LogInformation("Successfully parsed {Count} categories from OpenAI response", 
                GetParsedCategoriesCount(results));
            return results;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse flat structure");
            return null;
        }
    }

    /// <summary>
    /// Attempts to get a nested category from the JSON structure.
    /// </summary>
    private bool TryGetNestedCategory(JsonElement root, string sectionName, string categoryName, out JsonElement element)
    {
        element = default;

        if (!root.TryGetProperty(sectionName, out JsonElement section))
            return false;

        return section.TryGetProperty(categoryName, out element);
    }

    /// <summary>
    /// Checks if a JSON element matches an expected category name.
    /// </summary>
    private bool IsCategoryMatch(JsonElement categoryElement, string expectedCategoryName)
    {
        if (categoryElement.TryGetProperty("category", out JsonElement categoryProp))
        {
            string? categoryValue = categoryProp.GetString();
            return !string.IsNullOrEmpty(categoryValue) && 
                   categoryValue.Replace("_", "").Replace("-", "").Replace(" ", "")
                   .Equals(expectedCategoryName.Replace("_", "").Replace("-", "").Replace(" ", ""), 
                           StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>
    /// Parses a category winner from a JSON element.
    /// </summary>
    private CategoryWinner ParseCategoryWinner(JsonElement element)
    {
        return new CategoryWinner
        {
            Speaker = GetStringProperty(element, "Speaker") ?? GetStringProperty(element, "speaker") ?? "Unknown",
            Timestamp = GetStringProperty(element, "Timestamp") ?? GetStringProperty(element, "timestamp") ?? "0:00",
            Quote = GetStringProperty(element, "Quote") ?? GetStringProperty(element, "quote") ?? "No quote available",
            Setup = GetStringProperty(element, "Setup") ?? GetStringProperty(element, "setup") ?? "",
            GroupReaction = GetStringProperty(element, "GroupReaction") ?? GetStringProperty(element, "group_reaction") ?? "",
            WhyItsGreat = GetStringProperty(element, "WhyItsGreat") ?? GetStringProperty(element, "why_its_great") ?? "",
            AudioQuality = ParseAudioQuality(GetStringProperty(element, "AudioQualityString") ?? GetStringProperty(element, "audio_quality")),
            EntertainmentScore = GetIntProperty(element, "EntertainmentScore") ?? GetIntProperty(element, "entertainment_score") ?? 5
        };
    }

    /// <summary>
    /// Parses a Top 5 list from a JSON element.
    /// </summary>
    private TopFiveList ParseTopFiveList(JsonElement element, string title)
    {
        TopFiveList topFive = new TopFiveList(); // TopFiveList doesn't have Title property

        if ((element.TryGetProperty("Entries", out JsonElement entriesElement) || 
             element.TryGetProperty("entries", out entriesElement)) && 
            entriesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in entriesElement.EnumerateArray())
            {
                TopFiveEntry topFiveEntry = new TopFiveEntry
                {
                    Speaker = GetStringProperty(entry, "Speaker") ?? GetStringProperty(entry, "speaker") ?? "Unknown",
                    Timestamp = GetStringProperty(entry, "Timestamp") ?? GetStringProperty(entry, "timestamp") ?? "0:00",
                    Quote = GetStringProperty(entry, "Quote") ?? GetStringProperty(entry, "quote") ?? "No quote available",
                    Context = GetStringProperty(entry, "Context") ?? GetStringProperty(entry, "context") ?? GetStringProperty(entry, "setup") ?? "",
                    AudioQuality = ParseAudioQuality(GetStringProperty(entry, "AudioQualityString") ?? GetStringProperty(entry, "audio_quality")),
                    Score = GetIntProperty(entry, "Score") ?? GetIntProperty(entry, "entertainment_score") ?? GetIntProperty(entry, "score") ?? 5,
                    Reasoning = GetStringProperty(entry, "Reasoning") ?? GetStringProperty(entry, "why_its_great") ?? GetStringProperty(entry, "reasoning") ?? "",
                    SourceAudioFile = GetStringProperty(entry, "source_audio_file") ?? ""
                };

                topFive.Entries.Add(topFiveEntry);
            }
        }

        return topFive;
    }

    /// <summary>
    /// Creates fallback results when parsing fails completely.
    /// </summary>
    private CategoryResults CreateFallbackResults()
    {
        return new CategoryResults
        {
            BestJoke = new CategoryWinner
            {
                Speaker = "Unknown",
                Timestamp = "0:00",
                Quote = "[Analysis parsing failed - raw response could not be interpreted]",
                Setup = "Technical issue during analysis",
                GroupReaction = "System error",
                WhyItsGreat = "This is a fallback result due to parsing issues",
                AudioQuality = AudioQuality.Clear,
                EntertainmentScore = 1
            }
        };
    }

    /// <summary>
    /// Helper method to get string property from JSON element.
    /// </summary>
    private string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement prop) ? prop.GetString() : null;
    }

    /// <summary>
    /// Helper method to get integer property from JSON element.
    /// </summary>
    private int? GetIntProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out JsonElement prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int value))
                return value;
        }
        return null;
    }

    /// <summary>
    /// Parses audio quality from string value.
    /// </summary>
    private AudioQuality ParseAudioQuality(string? quality)
    {
        if (string.IsNullOrEmpty(quality)) return AudioQuality.Clear;

        return quality.ToLowerInvariant() switch
        {
            "clear" => AudioQuality.Clear,
            "muffled" => AudioQuality.Muffled,
            "background_noise" or "backgroundnoise" => AudioQuality.BackgroundNoise,
            _ => AudioQuality.Clear
        };
    }

    /// <summary>
    /// Parses opening questions from a JSON element.
    /// </summary>
    private List<QuestionAnswer> ParseOpeningQuestions(JsonElement element)
    {
        List<QuestionAnswer> questions = new List<QuestionAnswer>();

        if ((element.TryGetProperty("Questions", out JsonElement questionsElement) || 
             element.TryGetProperty("questions", out questionsElement)) && 
            questionsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement question in questionsElement.EnumerateArray())
            {
                QuestionAnswer questionAnswer = new QuestionAnswer
                {
                    Question = GetStringProperty(question, "Question") ?? GetStringProperty(question, "question") ?? "",
                    Speaker = GetStringProperty(question, "Speaker") ?? GetStringProperty(question, "speaker") ?? "Unknown",
                    Answer = GetStringProperty(question, "Answer") ?? GetStringProperty(question, "answer") ?? "",
                    Timestamp = GetStringProperty(question, "Timestamp") ?? GetStringProperty(question, "timestamp") ?? "0:00",
                    EntertainmentValue = GetIntProperty(question, "EntertainmentValue") ?? GetIntProperty(question, "entertainment_value") ?? 5,
                    AudioClipUrl = "" // Not provided in OpenAI response
                };

                questions.Add(questionAnswer);
            }
        }

        return questions;
    }

    /// <summary>
    /// Gets count of successfully parsed categories for logging.
    /// </summary>
    private int GetParsedCategoriesCount(CategoryResults results)
    {
        int count = 0;
        if (results.BestJoke != null) count++;
        if (results.HottestTake != null) count++;
        if (results.MostOffensiveTake != null) count++;
        if (results.BiggestArgumentStarter != null) count++;
        if (results.BestRoast != null) count++;
        if (results.FunniestRandomTangent != null) count++;
        if (results.MostPassionateDefense != null) count++;
        if (results.BiggestUnanimousReaction != null) count++;
        if (results.MostBoringStatement != null) count++;
        if (results.BestPlotTwistRevelation != null) count++;
        if (results.MovieSnobMoment != null) count++;
        if (results.GuiltyPleasureAdmission != null) count++;
        if (results.QuietestPersonBestMoment != null) count++;
        if (results.FunniestSentences != null && results.FunniestSentences.Entries.Count > 0) count++;
        if (results.MostBlandComments != null && results.MostBlandComments.Entries.Count > 0) count++;
        if (results.InitialQuestions != null && results.InitialQuestions.Count > 0) count++;
        return count;
    }
}