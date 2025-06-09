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

        // Try different parsing strategies
        CategoryResults? results = TryParseNestedStructure(cleanedResult) ?? TryParseFlatStructure(cleanedResult);

        if (results == null)
        {
            _logger.LogWarning("Failed to parse analysis result, using fallback structure");
            return CreateFallbackResults();
        }

        _logger.LogInformation("Successfully parsed analysis result");
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

            // Try to find categories directly at root level
            foreach (JsonProperty property in root.EnumerateObject())
            {
                string categoryName = property.Name.ToLowerInvariant().Replace("_", "").Replace("-", "");
                
                if (IsCategoryMatch(property.Value, "best_joke") || categoryName.Contains("bestjoke"))
                    results.BestJoke = ParseCategoryWinner(property.Value);
                else if (IsCategoryMatch(property.Value, "hottest_take") || categoryName.Contains("hottesttake"))
                    results.HottestTake = ParseCategoryWinner(property.Value);
                else if (IsCategoryMatch(property.Value, "most_offensive_take") || categoryName.Contains("offensivetake"))
                    results.MostOffensiveTake = ParseCategoryWinner(property.Value);
                else if (IsCategoryMatch(property.Value, "best_plot_twist") || categoryName.Contains("plottwist"))
                    results.BestPlotTwistRevelation = ParseCategoryWinner(property.Value);
                else if (IsCategoryMatch(property.Value, "biggest_argument_starter") || categoryName.Contains("argumentstarter"))
                    results.BiggestArgumentStarter = ParseCategoryWinner(property.Value);
                else if (categoryName.Contains("funniest") && categoryName.Contains("sentences"))
                    results.FunniestSentences = ParseTopFiveList(property.Value, "Funniest Sentences");
                else if (categoryName.Contains("bland") && categoryName.Contains("comments"))
                    results.MostBlandComments = ParseTopFiveList(property.Value, "Most Bland Comments");
            }

            return results;
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse flat structure");
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
            Speaker = GetStringProperty(element, "speaker") ?? "Unknown",
            Timestamp = GetStringProperty(element, "timestamp") ?? "0:00",
            Quote = GetStringProperty(element, "quote") ?? "No quote available",
            Setup = GetStringProperty(element, "setup") ?? "",
            GroupReaction = GetStringProperty(element, "group_reaction") ?? "",
            WhyItsGreat = GetStringProperty(element, "why_its_great") ?? "",
            AudioQuality = ParseAudioQuality(GetStringProperty(element, "audio_quality")),
            EntertainmentScore = GetIntProperty(element, "entertainment_score") ?? 5
        };
    }

    /// <summary>
    /// Parses a Top 5 list from a JSON element.
    /// </summary>
    private TopFiveList ParseTopFiveList(JsonElement element, string title)
    {
        TopFiveList topFive = new TopFiveList(); // TopFiveList doesn't have Title property

        if (element.TryGetProperty("entries", out JsonElement entriesElement) && entriesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in entriesElement.EnumerateArray())
            {
                TopFiveEntry topFiveEntry = new TopFiveEntry
                {
                    Speaker = GetStringProperty(entry, "speaker") ?? "Unknown",
                    Timestamp = GetStringProperty(entry, "timestamp") ?? "0:00",
                    Quote = GetStringProperty(entry, "quote") ?? "No quote available",
                    Context = GetStringProperty(entry, "context") ?? GetStringProperty(entry, "setup") ?? "",
                    AudioQuality = ParseAudioQuality(GetStringProperty(entry, "audio_quality")),
                    Score = GetIntProperty(entry, "entertainment_score") ?? GetIntProperty(entry, "score") ?? 5,
                    Reasoning = GetStringProperty(entry, "why_its_great") ?? GetStringProperty(entry, "reasoning") ?? "",
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
}