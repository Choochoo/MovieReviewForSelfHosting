using System.Text.RegularExpressions;
using MovieReviewApp.Application.Services.Analysis;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Service responsible for generating award categories using AI.
/// Uses OpenAI to create unique, creative award categories for the movie club.
/// </summary>
public class CategoryGenerationService
{
    private readonly OpenAIApiService _openAiService;
    private readonly AwardQuestionService _awardQuestionService;
    private readonly ILogger<CategoryGenerationService> _logger;

    private const string CATEGORY_GENERATION_PROMPT = @"Generate 40 unique award categories for our annual movie club awards. We'll vote as a group to narrow these down to our final 12.

Guidelines:
- Each category should describe a feeling, reaction, or lasting impression the movie left on the viewer
- Keep them conversational and relatable (like ""The one that..."" or ""Most likely to..."")
- Avoid standard award show categories (no ""Best Picture"" or ""Best Acting"")
- No genre-specific categories (no ""Best Horror"" or ""Best Comedy"")
- Each category name should be self-explanatory without needing additional description
- Give us variety: some funny, some heartfelt, some weird, some that capture those hard-to-describe movie moments
- Don't repeat the same idea with different wording

Examples of the tone I'm looking for:
- ""The one that gave you the most nightmares""
- ""The one that put a smile on your face weeks after watching""

Output format:
Return exactly 40 categories as a numbered list. Each category should be a short phrase (under 15 words). No descriptions or explanations needed.";

    public CategoryGenerationService(
        OpenAIApiService openAiService,
        AwardQuestionService awardQuestionService,
        ILogger<CategoryGenerationService> logger)
    {
        _openAiService = openAiService;
        _awardQuestionService = awardQuestionService;
        _logger = logger;
    }

    /// <summary>
    /// Generates 40 unique award categories using AI
    /// </summary>
    /// <returns>List of 40 category names, or empty list if generation fails</returns>
    public async Task<List<string>> GenerateCategoriesAsync()
    {
        List<string> existingCategories = await GetExistingAwardCategoriesAsync();

        if (!_openAiService.IsConfigured)
        {
            _logger.LogWarning("OpenAI API not configured - returning default categories");
            return FilterSimilarCategories(GetDefaultCategories(), existingCategories);
        }

        try
        {
            _logger.LogInformation("Generating award categories using AI...");

            string? response = await _openAiService.ExecutePromptAsync(
                BuildPromptWithExclusions(existingCategories),
                maxTokens: 2000,
                temperature: 0.8); // Higher temperature for more creativity

            if (string.IsNullOrEmpty(response))
            {
                _logger.LogWarning("Empty response from AI, using default categories");
                return FilterSimilarCategories(GetDefaultCategories(), existingCategories);
            }

            List<string> categories = ParseCategoriesFromResponse(response);
            categories = FilterSimilarCategories(categories, existingCategories);

            if (categories.Count < 40)
            {
                _logger.LogWarning(
                    "AI generated only {Count} categories, expected 40. Padding with defaults.",
                    categories.Count);

                // Pad with defaults if needed
                List<string> defaults = FilterSimilarCategories(GetDefaultCategories(), existingCategories);
                int needed = 40 - categories.Count;
                categories.AddRange(defaults.Where(d => !categories.Contains(d, StringComparer.OrdinalIgnoreCase)).Take(needed));
            }

            _logger.LogInformation("Successfully generated {Count} award categories", categories.Count);
            return categories.Take(40).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate categories from AI, using defaults");
            return FilterSimilarCategories(GetDefaultCategories(), existingCategories);
        }
    }

    /// <summary>
    /// Parses the numbered list response from AI into a clean list of category names
    /// </summary>
    private List<string> ParseCategoriesFromResponse(string response)
    {
        List<string> categories = new List<string>();

        // Split by lines
        string[] lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            string trimmed = line.Trim();

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // Remove numbering (e.g., "1.", "1)", "1 -", etc.)
            string cleaned = Regex.Replace(trimmed, @"^\d+[\.\)\-\:]\s*", "");

            // Remove quotes if present
            cleaned = cleaned.Trim('"', '"', '"', '\'');

            // Skip if too short or too long
            if (cleaned.Length < 5 || cleaned.Length > 100)
                continue;

            categories.Add(cleaned);
        }

        return categories;
    }

    private async Task<List<string>> GetExistingAwardCategoriesAsync()
    {
        List<AwardQuestion> existingQuestions = await _awardQuestionService.GetAllAsync();
        return existingQuestions
            .Select(q => q.Question)
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string BuildPromptWithExclusions(List<string> existingCategories)
    {
        if (existingCategories.Count == 0)
            return CATEGORY_GENERATION_PROMPT;

        string exclusionList = string.Join("\n", existingCategories.Select(c => $"- {c}"));
        return $"{CATEGORY_GENERATION_PROMPT}\n\nIMPORTANT: Do NOT generate categories similar to these existing ones:\n{exclusionList}";
    }

    private List<string> FilterSimilarCategories(List<string> generated, List<string> existing)
    {
        List<string> filtered = new();

        foreach (string category in generated)
        {
            if (!IsSimilarToAny(category, existing) && !IsSimilarToAny(category, filtered))
            {
                filtered.Add(category);
            }
            else
            {
                _logger.LogInformation("Filtered out similar category: {Category}", category);
            }
        }

        return filtered;
    }

    private bool IsSimilarToAny(string category, List<string> existingList)
    {
        string normalizedCategory = NormalizeForComparison(category);

        foreach (string existing in existingList)
        {
            string normalizedExisting = NormalizeForComparison(existing);

            if (normalizedCategory == normalizedExisting)
                return true;

            if (CalculateWordOverlap(normalizedCategory, normalizedExisting) > 0.7)
                return true;

            if (ContainsSameKeyPhrases(normalizedCategory, normalizedExisting))
                return true;
        }

        return false;
    }

    private string NormalizeForComparison(string text)
    {
        return Regex.Replace(text.ToLowerInvariant(), @"[^\w\s]", "").Trim();
    }

    private double CalculateWordOverlap(string text1, string text2)
    {
        HashSet<string> words1 = new(text1.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        HashSet<string> words2 = new(text2.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (words1.Count == 0 || words2.Count == 0)
            return 0;

        int commonWords = words1.Intersect(words2).Count();
        int totalUniqueWords = words1.Union(words2).Count();

        return (double)commonWords / totalUniqueWords;
    }

    private bool ContainsSameKeyPhrases(string text1, string text2)
    {
        string[] keyPhrases =
        {
            "nightmare", "cry", "ugly cry", "smile", "laugh", "quote", "rewatch",
            "watch again", "recommend", "ending", "opening", "soundtrack", "music",
            "villain", "surprise", "disappoint", "waste of time", "too long",
            "fell asleep", "speechless", "sequel", "chemistry", "fever dream",
            "memorable scene", "best opening"
        };

        foreach (string phrase in keyPhrases)
        {
            if (text1.Contains(phrase) && text2.Contains(phrase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns a set of default categories if AI generation fails
    /// </summary>
    private List<string> GetDefaultCategories()
    {
        return new List<string>
        {
            "The one that gave you the most nightmares",
            "The one that put a smile on your face weeks after watching",
            "The one you'd watch again right now",
            "The one that made you ugly cry",
            "The one with the most quotable lines",
            "The one that made you text someone 'you HAVE to watch this'",
            "The one that aged poorly since you watched it",
            "The one that surprised you the most",
            "The one that felt way too long",
            "The one that ended too soon",
            "The one you fell asleep during",
            "The one with the best soundtrack",
            "The one that made you think differently about something",
            "The one you'd never watch again",
            "The one that was better than expected",
            "The one that was worse than expected",
            "The one with the most satisfying ending",
            "The one with the most disappointing ending",
            "The one that made you uncomfortable in the best way",
            "The one that felt like a waste of time",
            "The one you'd recommend to your parents",
            "The one you'd never recommend to your parents",
            "The one with the best villain",
            "The one that made you want to visit a new place",
            "The one that changed your perspective",
            "The one with the most memorable scene",
            "The one that felt the most relatable",
            "The one that was pure escapism",
            "The one that kept you guessing",
            "The one with the best opening scene",
            "The one you keep bringing up in conversations",
            "The one that deserves a sequel",
            "The one that should never have a sequel",
            "The one that made you hungry",
            "The one with the best chemistry between characters",
            "The one that felt like a fever dream",
            "The one that made you appreciate your life more",
            "The one that was accidentally hilarious",
            "The one with the most rewatch potential",
            "The one that left you speechless"
        };
    }
}
