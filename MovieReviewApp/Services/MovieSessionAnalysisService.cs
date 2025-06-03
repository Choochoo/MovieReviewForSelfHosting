using MovieReviewApp.Models;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MovieReviewApp.Services;

public class MovieSessionAnalysisService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MovieSessionAnalysisService> _logger;
    private readonly string _openAiApiKey;

    public MovieSessionAnalysisService(HttpClient httpClient, IConfiguration configuration, ILogger<MovieSessionAnalysisService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _openAiApiKey = _configuration["OpenAI:ApiKey"] ?? throw new InvalidOperationException("OpenAI API key not found in configuration");
        
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_openAiApiKey}");
    }

    public async Task<CategoryResults> AnalyzeSessionAsync(MovieSession session)
    {
        try
        {
            // Combine all transcripts with speaker information
            var combinedTranscript = BuildCombinedTranscript(session);

            if (string.IsNullOrEmpty(combinedTranscript))
            {
                throw new Exception("No transcript content available for analysis");
            }

            // Create the analysis prompt based on processaudio.md specifications
            var analysisPrompt = CreateAnalysisPrompt(session.MovieTitle, session.Date, session.ParticipantsPresent, combinedTranscript);

            // Call OpenAI to analyze the transcript
            var analysisResult = await CallOpenAIForAnalysis(analysisPrompt);

            // Parse the AI response into CategoryResults
            var categoryResults = ParseAnalysisResult(analysisResult);

            _logger.LogInformation("Successfully analyzed session {SessionId} for movie {MovieTitle}", session.Id, session.MovieTitle);

            return categoryResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze session {SessionId}", session.Id);
            throw;
        }
    }

    private string BuildCombinedTranscript(MovieSession session)
    {
        var transcriptBuilder = new StringBuilder();

        // Group utterances by timestamp if we have detailed transcription data
        // For now, we'll work with the basic transcript text
        foreach (var audioFile in session.AudioFiles.Where(f => !string.IsNullOrEmpty(f.TranscriptText)))
        {
            var speakerLabel = audioFile.SpeakerNumber.HasValue ? $"Speaker{audioFile.SpeakerNumber}" : "Unknown Speaker";

            if (audioFile.IsMasterRecording)
            {
                transcriptBuilder.AppendLine("=== MASTER RECORDING ===");
                transcriptBuilder.AppendLine(audioFile.TranscriptText);
                transcriptBuilder.AppendLine();
            }
            else
            {
                transcriptBuilder.AppendLine($"=== {speakerLabel} INDIVIDUAL MIC ===");
                transcriptBuilder.AppendLine(audioFile.TranscriptText);
                transcriptBuilder.AppendLine();
            }
        }

        return transcriptBuilder.ToString();
    }

    private string CreateAnalysisPrompt(string movieTitle, DateTime sessionDate, List<string> participants, string transcript)
    {
        var participantsList = string.Join(", ", participants);

        return $@"
You are analyzing a movie discussion group's recorded conversation for entertainment value. This is a group of friends discussing the movie ""{movieTitle}"" on {sessionDate:MMMM dd, yyyy}.

Participants present: {participantsList}

Your task is to identify the most entertaining moments across these specific categories. Each category is designed to capture different aspects of group dynamics and entertainment value.

## ANALYSIS CATEGORIES:

### CONTROVERSIAL & DEBATE CATEGORIES
1. **Most Offensive Take** - Statements about representation, politics, social issues that sparked pushback
2. **Hottest Take** - Unpopular opinions against consensus (loving hated movies, hating beloved ones)
3. **Biggest Argument Starter** - Comments leading to extended back-and-forth, raised voices

### HUMOR & ENTERTAINMENT CATEGORIES  
4. **Best Joke** - Intentional humor with clear positive reactions
5. **Best Roast** - Clever, harsh movie criticism that's more funny than mean
6. **Funniest Random Tangent** - Off-topic conversations that became hilarious

### REACTION & DYNAMICS CATEGORIES
7. **Most Passionate Defense** - Strong emotional defense of unpopular opinions
8. **Biggest Unanimous Reaction** - Moments where everyone had the same strong response
9. **Most Boring Statement** - Comments that killed conversation energy
10. **Best Plot Twist Revelation** - Surprising movie insights others missed

### INDIVIDUAL PERSONALITY CATEGORIES
11. **Movie Snob Moment** - Overly academic/pretentious analysis
12. **Guilty Pleasure Admission** - Confessing enjoyment of something group thinks is bad
13. **Quietest Person's Best Moment** - Usually quiet member making standout comment

### TOP 5 LISTS
14. **Top 5 Funniest Sentences** - Individual sentences that are genuinely hilarious
15. **Top 5 Most Bland Comments** - The most boring, energy-killing statements

## INSTRUCTIONS:

1. **Speaker Consistency**: Remember that Speaker1-6 represent consistent people across sessions
2. **Context Matters**: Include setup that led to the moment
3. **Group Reactions**: Note how others responded (laughter, disagreement, etc.)
4. **Entertainment Value**: Focus on replay value and what makes it funny/interesting
5. **Timestamps**: Extract or estimate timestamps when possible

## RESPONSE FORMAT:

Respond with a JSON object containing each category. For regular categories (1-13), return an object with the following properties: speaker, timestamp, quote, setup, groupReaction, whyItsGreat, audioQuality, entertainmentScore, runnersUp (an array of objects with speaker, timestamp, briefDescription, place). 

For Top 5 lists (14-15), return an object with an ""entries"" array. Each entry should have: rank (1-5), speaker, timestamp, quote, context, audioQuality, score (1-10), reasoning, and estimated start/end times in seconds from the beginning of that audio file.

If a category has no clear moments, use null for that category.

## TRANSCRIPT TO ANALYZE:

{transcript}

Please analyze this transcript and identify the best moments for each category. Focus on entertainment value and what would be fun to revisit later.";
    }

    private async Task<string> CallOpenAIForAnalysis(string prompt)
    {
        var requestBody = new
        {
            model = "gpt-4",
            messages = new[]
            {
                new { role = "system", content = "You are an expert at analyzing group conversations for entertainment value. You understand humor, group dynamics, and what makes moments memorable." },
                new { role = "user", content = prompt }
            },
            max_tokens = 4000,
            temperature = 0.7
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var responseObj = JsonSerializer.Deserialize<OpenAIResponse>(responseJson);

        return responseObj?.choices?.FirstOrDefault()?.message?.content ??
               throw new Exception("No response content from OpenAI");
    }

    private CategoryResults ParseAnalysisResult(string analysisResult)
    {
        try
        {
            // Extract JSON from the response (in case there's additional text)
            var jsonMatch = Regex.Match(analysisResult, @"\{[\s\S]*\}", RegexOptions.Multiline);
            var jsonContent = jsonMatch.Success ? jsonMatch.Value : analysisResult;

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var analysisData = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent, options);

            var categoryResults = new CategoryResults();

            // Map each category from the AI response
            categoryResults.BestJoke = ParseCategoryWinner(analysisData, "bestJoke");
            categoryResults.HottestTake = ParseCategoryWinner(analysisData, "hottestTake");
            categoryResults.BiggestArgumentStarter = ParseCategoryWinner(analysisData, "biggestArgumentStarter");
            categoryResults.BestRoast = ParseCategoryWinner(analysisData, "bestRoast");
            categoryResults.FunniestRandomTangent = ParseCategoryWinner(analysisData, "funniestRandomTangent");
            categoryResults.MostPassionateDefense = ParseCategoryWinner(analysisData, "mostPassionateDefense");
            categoryResults.BiggestUnanimousReaction = ParseCategoryWinner(analysisData, "biggestUnanimousReaction");
            categoryResults.MostBoringStatement = ParseCategoryWinner(analysisData, "mostBoringStatement");
            categoryResults.BestPlotTwistRevelation = ParseCategoryWinner(analysisData, "bestPlotTwistRevelation");
            categoryResults.MovieSnobMoment = ParseCategoryWinner(analysisData, "movieSnobMoment");
            categoryResults.GuiltyPleasureAdmission = ParseCategoryWinner(analysisData, "guiltyPleasureAdmission");
            categoryResults.QuietestPersonBestMoment = ParseCategoryWinner(analysisData, "quietestPersonBestMoment");
            categoryResults.MostOffensiveTake = ParseCategoryWinner(analysisData, "mostOffensiveTake");
            
            // Parse Top 5 lists
            categoryResults.FunniestSentences = ParseTopFiveList(analysisData, "funniestSentences");
            categoryResults.MostBlandComments = ParseTopFiveList(analysisData, "mostBlandComments");

            return categoryResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse analysis result: {Result}", analysisResult);

            // Return empty results rather than failing completely
            return new CategoryResults();
        }
    }

    private CategoryWinner? ParseCategoryWinner(Dictionary<string, object> data, string categoryKey)
    {
        try
        {
            if (!data.ContainsKey(categoryKey) || data[categoryKey] == null)
                return null;

            var categoryData = JsonSerializer.Deserialize<Dictionary<string, object>>(data[categoryKey].ToString()!);
            if (categoryData == null) return null;

            var winner = new CategoryWinner
            {
                Speaker = GetStringValue(categoryData, "speaker") ?? "Unknown",
                Timestamp = GetStringValue(categoryData, "timestamp") ?? "0:00",
                Quote = GetStringValue(categoryData, "quote") ?? "",
                Setup = GetStringValue(categoryData, "setup") ?? "",
                GroupReaction = GetStringValue(categoryData, "groupReaction") ?? "",
                WhyItsGreat = GetStringValue(categoryData, "whyItsGreat") ?? "",
                AudioQuality = ParseAudioQuality(GetStringValue(categoryData, "audioQuality")),
                EntertainmentScore = GetIntValue(categoryData, "entertainmentScore") ?? 5
            };

            // Parse runners up if available
            if (categoryData.ContainsKey("runnersUp") && categoryData["runnersUp"] != null)
            {
                try
                {
                    var runnersUpArray = JsonSerializer.Deserialize<JsonElement[]>(categoryData["runnersUp"].ToString()!);
                    if (runnersUpArray != null)
                    {
                        winner.RunnersUp = runnersUpArray.Select(ru => new RunnerUp
                        {
                            Speaker = ru.TryGetProperty("speaker", out var speaker) ? speaker.GetString() ?? "Unknown" : "Unknown",
                            Timestamp = ru.TryGetProperty("timestamp", out var timestamp) ? timestamp.GetString() ?? "0:00" : "0:00",
                            BriefDescription = ru.TryGetProperty("briefDescription", out var desc) ? desc.GetString() ?? "" : "",
                            Place = ru.TryGetProperty("place", out var place) ? place.GetInt32() : 2
                        }).ToList();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse runners up for category {Category}", categoryKey);
                }
            }

            return winner;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse category {Category}", categoryKey);
            return null;
        }
    }

    private string? GetStringValue(Dictionary<string, object> dict, string key)
    {
        return dict.ContainsKey(key) ? dict[key]?.ToString() : null;
    }

    private int? GetIntValue(Dictionary<string, object> dict, string key)
    {
        if (!dict.ContainsKey(key) || dict[key] == null) return null;

        if (int.TryParse(dict[key].ToString(), out var intValue))
            return intValue;

        return null;
    }

    private AudioQuality ParseAudioQuality(string? quality)
    {
        return quality?.ToLower() switch
        {
            "clear" => AudioQuality.Clear,
            "muffled" => AudioQuality.Muffled,
            "background noise" or "backgroundnoise" => AudioQuality.BackgroundNoise,
            _ => AudioQuality.Clear
        };
    }

    private TopFiveList? ParseTopFiveList(Dictionary<string, object> data, string categoryKey)
    {
        try
        {
            if (!data.ContainsKey(categoryKey) || data[categoryKey] == null)
                return null;

            var categoryData = JsonSerializer.Deserialize<Dictionary<string, object>>(data[categoryKey].ToString()!);
            if (categoryData == null || !categoryData.ContainsKey("entries") || categoryData["entries"] == null)
                return null;

            var entriesArray = JsonSerializer.Deserialize<JsonElement[]>(categoryData["entries"].ToString()!);
            if (entriesArray == null) return null;

            var topFive = new TopFiveList();
            
            foreach (var entryElement in entriesArray.Take(5))
            {
                var entry = new TopFiveEntry
                {
                    Rank = entryElement.TryGetProperty("rank", out var rank) ? rank.GetInt32() : 0,
                    Speaker = entryElement.TryGetProperty("speaker", out var speaker) ? speaker.GetString() ?? "Unknown" : "Unknown",
                    Timestamp = entryElement.TryGetProperty("timestamp", out var timestamp) ? timestamp.GetString() ?? "0:00" : "0:00",
                    Quote = entryElement.TryGetProperty("quote", out var quote) ? quote.GetString() ?? "" : "",
                    Context = entryElement.TryGetProperty("context", out var context) ? context.GetString() ?? "" : "",
                    AudioQuality = ParseAudioQuality(entryElement.TryGetProperty("audioQuality", out var aq) ? aq.GetString() : null),
                    Score = entryElement.TryGetProperty("score", out var score) ? score.GetDouble() : 5.0,
                    Reasoning = entryElement.TryGetProperty("reasoning", out var reasoning) ? reasoning.GetString() ?? "" : "",
                    StartTimeSeconds = entryElement.TryGetProperty("startTimeSeconds", out var start) ? start.GetDouble() : 0,
                    EndTimeSeconds = entryElement.TryGetProperty("endTimeSeconds", out var end) ? end.GetDouble() : 0
                };

                topFive.Entries.Add(entry);
            }

            return topFive.Entries.Any() ? topFive : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse top five list for category {Category}", categoryKey);
            return null;
        }
    }

    public SessionStats GenerateSessionStats(MovieSession session, CategoryResults categoryResults)
    {
        var stats = new SessionStats
        {
            TotalDuration = CalculateTotalDuration(session),
            TechnicalQuality = AssessTechnicalQuality(session),
            AttendancePattern = $"{session.ParticipantsPresent.Count}/{session.ParticipantsPresent.Count + session.ParticipantsAbsent.Count} regular members present"
        };

        // Calculate energy level based on categories found
        var highlightCount = CountHighlights(categoryResults);
        stats.HighlightMoments = highlightCount;

        stats.EnergyLevel = highlightCount switch
        {
            >= 10 => EnergyLevel.High,
            >= 5 => EnergyLevel.Medium,
            _ => EnergyLevel.Low
        };

        stats.BestMomentsSummary = GenerateBestMomentsSummary(categoryResults, stats.EnergyLevel);

        return stats;
    }

    private string CalculateTotalDuration(MovieSession session)
    {
        var maxDuration = session.AudioFiles
            .Where(f => f.Duration.HasValue)
            .Select(f => f.Duration!.Value.TotalMinutes)
            .DefaultIfEmpty(0)
            .Max();

        if (maxDuration >= 60)
        {
            var hours = (int)(maxDuration / 60);
            var minutes = (int)(maxDuration % 60);
            return $"{hours}h {minutes}m";
        }

        return $"{(int)maxDuration}m";
    }

    private string AssessTechnicalQuality(MovieSession session)
    {
        var totalFiles = session.AudioFiles.Count;
        if (totalFiles == 0) return "Unknown";

        var clearFiles = session.AudioFiles.Count(f => !string.IsNullOrEmpty(f.TranscriptText));
        var percentage = (double)clearFiles / totalFiles * 100;

        return percentage switch
        {
            >= 90 => "Excellent - all audio clear",
            >= 70 => "Good - most audio clear",
            >= 50 => "Fair - some audio issues",
            _ => "Poor - significant audio problems"
        };
    }

    private int CountHighlights(CategoryResults results)
    {
        var count = 0;

        if (results.BestJoke != null) count++;
        if (results.HottestTake != null) count++;
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
        if (results.MostOffensiveTake != null) count++;
        if (results.FunniestSentences?.Entries.Any() == true) count += results.FunniestSentences.Entries.Count;
        if (results.MostBlandComments?.Entries.Any() == true) count += results.MostBlandComments.Entries.Count;

        return count;
    }

    private string GenerateBestMomentsSummary(CategoryResults results, EnergyLevel energyLevel)
    {
        var highlights = new List<string>();

        if (results.BestJoke != null) highlights.Add("great comedy moments");
        if (results.BiggestArgumentStarter != null) highlights.Add("passionate debates");
        if (results.HottestTake != null) highlights.Add("controversial opinions");
        if (results.BestRoast != null) highlights.Add("savage roasts");
        if (results.FunniestRandomTangent != null) highlights.Add("hilarious tangents");

        var energyDescription = energyLevel switch
        {
            EnergyLevel.High => "High energy session with",
            EnergyLevel.Medium => "Good discussion featuring",
            EnergyLevel.Low => "Quieter session with some",
            _ => "Session featuring"
        };

        if (highlights.Any())
        {
            return $"{energyDescription} {string.Join(", ", highlights)}.";
        }

        return "Session analysis complete with various discussion points identified.";
    }
}

// DTO for OpenAI API response
public class OpenAIResponse
{
    public OpenAIChoice[]? choices { get; set; }
}

public class OpenAIChoice
{
    public OpenAIMessage? message { get; set; }
}

public class OpenAIMessage
{
    public string? content { get; set; }
}