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
        _logger.LogInformation("[ANALYSIS DEBUG] ResponseParsingService.ParseAnalysisResult starting");
        
        if (string.IsNullOrWhiteSpace(analysisResult))
        {
            _logger.LogWarning("[ANALYSIS DEBUG] Received empty analysis result");
            return new CategoryResults();
        }

        _logger.LogDebug("[ANALYSIS DEBUG] Parsing analysis result: {Length} characters", analysisResult.Length);

        // Remove any markdown formatting
        string cleanedResult = analysisResult.Replace("```json", "").Replace("```", "").Trim();
        
        // Log first 500 characters of the cleaned result for debugging
        _logger.LogDebug("[ANALYSIS DEBUG] Cleaned result preview: {Preview}", 
            cleanedResult.Length > 500 ? cleanedResult.Substring(0, 500) + "..." : cleanedResult);

        // Try different parsing strategies
        _logger.LogInformation("[ANALYSIS DEBUG] Attempting to parse with nested structure");
        CategoryResults? results = TryParseNestedStructure(cleanedResult);
        
        if (results == null)
        {
            _logger.LogInformation("[ANALYSIS DEBUG] Nested structure parsing failed, trying flat structure");
            results = TryParseFlatStructure(cleanedResult);
        }

        if (results == null)
        {
            _logger.LogWarning("[ANALYSIS DEBUG] Failed to parse analysis result, using fallback structure");
            _logger.LogDebug("[ANALYSIS DEBUG] Full analysis result that failed to parse: {Result}", cleanedResult);
            CategoryResults fallbackResults = CreateFallbackResults();
            _logger.LogInformation("[ANALYSIS DEBUG] Created fallback results");
            return fallbackResults;
        }

        int categoryCount = GetParsedCategoriesCount(results);
        _logger.LogInformation("[ANALYSIS DEBUG] Successfully parsed analysis result with {Count} categories", categoryCount);
        
        if (results != null)
        {
            _logger.LogInformation("[ANALYSIS DEBUG] Parsed results summary - BestJoke: {BestJoke}, HottestTake: {HottestTake}", 
                results.BestJoke?.Quote ?? "null", 
                results.HottestTake?.Quote ?? "null");
        }
        
        return results ?? new CategoryResults();
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
            bool foundAnyCategory = false;

            // Parse each category section
            if (TryGetNestedCategory(root, "comedy_categories", "best_joke", out JsonElement bestJoke))
            {
                results.BestJoke = ParseCategoryWinner(bestJoke);
                foundAnyCategory = true;
            }
            
            if (TryGetNestedCategory(root, "comedy_categories", "most_offensive_take", out JsonElement offensiveTake))
            {
                results.MostOffensiveTake = ParseCategoryWinner(offensiveTake);
                foundAnyCategory = true;
            }

            if (TryGetNestedCategory(root, "opinion_categories", "hottest_take", out JsonElement hottestTake))
            {
                results.HottestTake = ParseCategoryWinner(hottestTake);
                foundAnyCategory = true;
            }

            if (TryGetNestedCategory(root, "insight_categories", "best_plot_twist", out JsonElement plotTwist))
            {
                results.BestPlotTwistRevelation = ParseCategoryWinner(plotTwist);
                foundAnyCategory = true;
            }

            if (TryGetNestedCategory(root, "discussion_categories", "biggest_argument_starter", out JsonElement argumentStarter))
            {
                results.BiggestArgumentStarter = ParseCategoryWinner(argumentStarter);
                foundAnyCategory = true;
            }

            // Parse Top 5 lists
            if (TryGetNestedCategory(root, "top_5_lists", "funniest_sentences", out JsonElement funniestSentences))
            {
                results.FunniestSentences = ParseTopFiveList(funniestSentences, "Funniest Sentences");
                foundAnyCategory = true;
            }

            if (TryGetNestedCategory(root, "top_5_lists", "most_bland_comments", out JsonElement blandComments))
            {
                results.MostBlandComments = ParseTopFiveList(blandComments, "Most Bland Comments");
                foundAnyCategory = true;
            }

            // If no categories were found using nested structure, return null to try flat structure
            if (!foundAnyCategory)
            {
                _logger.LogDebug("[ANALYSIS DEBUG] Nested structure parsing found 0 categories, returning null to try flat parsing");
                return null;
            }

            _logger.LogDebug("[ANALYSIS DEBUG] Nested structure parsing found categories, returning results");
            return results;
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "[ANALYSIS DEBUG] Failed to parse nested structure due to JSON exception");
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
                    case "AIsUniqueObservations":
                        results.AIsUniqueObservations = ParseTopFiveList(property.Value, "AI's Unique Observations");
                        break;
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
        CategoryWinner winner = new CategoryWinner
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

        // Parse RunnersUp if they exist
        if ((element.TryGetProperty("RunnersUp", out JsonElement runnersUpElement) || 
             element.TryGetProperty("runners_up", out runnersUpElement)) && 
            runnersUpElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement runnerUpElement in runnersUpElement.EnumerateArray())
            {
                RunnerUp runnerUp = new RunnerUp
                {
                    Speaker = GetStringProperty(runnerUpElement, "Speaker") ?? GetStringProperty(runnerUpElement, "speaker") ?? "Unknown",
                    Timestamp = GetStringProperty(runnerUpElement, "Timestamp") ?? GetStringProperty(runnerUpElement, "timestamp") ?? "0:00",
                    BriefDescription = GetStringProperty(runnerUpElement, "BriefDescription") ?? GetStringProperty(runnerUpElement, "brief_description") ?? GetStringProperty(runnerUpElement, "Quote") ?? GetStringProperty(runnerUpElement, "quote") ?? "",
                    Place = GetIntProperty(runnerUpElement, "Place") ?? GetIntProperty(runnerUpElement, "place") ?? GetIntProperty(runnerUpElement, "Rank") ?? GetIntProperty(runnerUpElement, "rank") ?? 2
                };
                winner.RunnersUp.Add(runnerUp);
            }
        }

        return winner;
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
                    Rank = GetIntProperty(entry, "Rank") ?? GetIntProperty(entry, "rank") ?? 0,
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
        if (results.AIsUniqueObservations != null && results.AIsUniqueObservations.Entries.Count > 0) count++;
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