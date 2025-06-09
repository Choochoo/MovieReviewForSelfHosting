using MovieReviewApp.Models;
using System.Text.RegularExpressions;

namespace MovieReviewApp.Application.Services.Analysis;

/// <summary>
/// Service responsible for generating basic session statistics from movie session data and analysis results.
/// Provides simple metrics and summaries without complex analysis model dependencies.
/// </summary>
public class SimpleSessionStatsService
{
    private readonly ILogger<SimpleSessionStatsService> _logger;

    public SimpleSessionStatsService(ILogger<SimpleSessionStatsService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generates session statistics from movie session and category results.
    /// </summary>
    public SessionStats GenerateSessionStats(MovieSession session, CategoryResults categoryResults)
    {
        _logger.LogDebug("Generating session stats for session {SessionId}", session.Id);

        SessionStats stats = new SessionStats
        {
            TotalDuration = CalculateTotalDuration(session),
            EnergyLevel = DetermineEnergyLevel(session, categoryResults),
            TechnicalQuality = AssessTechnicalQuality(session),
            HighlightMoments = CountHighlightMoments(categoryResults),
            BestMomentsSummary = CreateBestMomentsSummary(categoryResults),
            AttendancePattern = CreateAttendancePattern(session)
        };

        // Generate basic conversation statistics
        PopulateConversationStats(stats, session);

        _logger.LogInformation("Generated session stats: {Duration}, {EnergyLevel}, {HighlightCount} highlights", 
            stats.TotalDuration, stats.EnergyLevel, stats.HighlightMoments);

        return stats;
    }

    /// <summary>
    /// Calculates the total duration of the session based on audio files.
    /// </summary>
    private string CalculateTotalDuration(MovieSession session)
    {
        double totalMinutes = session.AudioFiles
            .Where(f => f.Duration.HasValue)
            .Select(f => f.Duration!.Value.TotalMinutes)
            .DefaultIfEmpty(0)
            .Max(); // Take the longest recording (likely the master)

        if (totalMinutes >= 60)
        {
            int hours = (int)(totalMinutes / 60);
            int minutes = (int)(totalMinutes % 60);
            return $"{hours}h {minutes}m";
        }
        else
        {
            return $"{(int)totalMinutes}m";
        }
    }

    /// <summary>
    /// Determines the energy level of the session based on analysis results.
    /// </summary>
    private EnergyLevel DetermineEnergyLevel(MovieSession session, CategoryResults categoryResults)
    {
        int totalScore = 0;
        int scoreCount = 0;

        // Collect entertainment scores from analysis
        if (categoryResults.BestJoke != null)
        {
            totalScore += categoryResults.BestJoke.EntertainmentScore;
            scoreCount++;
        }

        if (categoryResults.HottestTake != null)
        {
            totalScore += categoryResults.HottestTake.EntertainmentScore;
            scoreCount++;
        }

        if (categoryResults.BestPlotTwistRevelation != null)
        {
            totalScore += categoryResults.BestPlotTwistRevelation.EntertainmentScore;
            scoreCount++;
        }

        if (categoryResults.FunniestSentences?.Entries.Any() == true)
        {
            totalScore += categoryResults.FunniestSentences.Entries.Sum(e => (int)e.Score);
            scoreCount += categoryResults.FunniestSentences.Entries.Count;
        }

        if (scoreCount == 0) return EnergyLevel.Medium;

        double averageScore = (double)totalScore / scoreCount;

        return averageScore switch
        {
            >= 8.0 => EnergyLevel.High,
            >= 6.0 => EnergyLevel.Medium,
            _ => EnergyLevel.Low
        };
    }

    /// <summary>
    /// Assesses the technical quality of audio recordings based on successful transcription rates.
    /// </summary>
    private string AssessTechnicalQuality(MovieSession session)
    {
        int totalFiles = session.AudioFiles.Count;
        if (totalFiles == 0) return "Unknown";

        int clearFiles = session.AudioFiles.Count(f => !string.IsNullOrEmpty(f.TranscriptText));
        double percentage = (double)clearFiles / totalFiles * 100;

        return percentage switch
        {
            >= 90 => "Excellent - all audio clear",
            >= 70 => "Good - most audio clear", 
            >= 50 => "Fair - some audio issues",
            _ => "Poor - significant audio problems"
        };
    }

    /// <summary>
    /// Counts the total number of highlight moments identified in the analysis.
    /// </summary>
    private int CountHighlightMoments(CategoryResults categoryResults)
    {
        int count = 0;

        if (categoryResults.BestJoke != null) count++;
        if (categoryResults.HottestTake != null) count++;
        if (categoryResults.MostOffensiveTake != null) count++;
        if (categoryResults.BestPlotTwistRevelation != null) count++;
        if (categoryResults.BiggestArgumentStarter != null) count++;

        if (categoryResults.FunniestSentences?.Entries.Any() == true)
            count += categoryResults.FunniestSentences.Entries.Count;

        if (categoryResults.MostBlandComments?.Entries.Any() == true)
            count += categoryResults.MostBlandComments.Entries.Count;

        return count;
    }

    /// <summary>
    /// Creates a summary of the best moments from the analysis.
    /// </summary>
    private string CreateBestMomentsSummary(CategoryResults categoryResults)
    {
        List<string> summaryParts = new List<string>();

        if (categoryResults.BestJoke != null)
        {
            summaryParts.Add($"Best joke by {categoryResults.BestJoke.Speaker}");
        }

        if (categoryResults.HottestTake != null)
        {
            summaryParts.Add($"Hot take from {categoryResults.HottestTake.Speaker}");
        }

        if (categoryResults.BestPlotTwistRevelation != null)
        {
            summaryParts.Add($"Great insight by {categoryResults.BestPlotTwistRevelation.Speaker}");
        }

        if (categoryResults.FunniestSentences?.Entries.Any() == true)
        {
            summaryParts.Add($"{categoryResults.FunniestSentences.Entries.Count} hilarious moments");
        }

        if (!summaryParts.Any())
        {
            return "Session analyzed but no standout moments identified";
        }

        return string.Join(", ", summaryParts) + ".";
    }

    /// <summary>
    /// Creates an attendance pattern description for the session.
    /// </summary>
    private string CreateAttendancePattern(MovieSession session)
    {
        int presentCount = session.ParticipantsPresent.Count;
        int totalCount = presentCount + session.ParticipantsAbsent.Count;

        if (totalCount == 0)
        {
            return "Unknown attendance";
        }

        return $"{presentCount}/{totalCount} regular members present";
    }

    /// <summary>
    /// Populates basic conversation statistics from transcript analysis.
    /// </summary>
    private void PopulateConversationStats(SessionStats stats, MovieSession session)
    {
        // Initialize conversation stats dictionaries
        stats.WordCounts = new Dictionary<string, int>();
        stats.QuestionCounts = new Dictionary<string, int>();
        stats.InterruptionCounts = new Dictionary<string, int>();
        stats.LaughterCounts = new Dictionary<string, int>();
        stats.CurseWordCounts = new Dictionary<string, int>();

        // Analyze transcripts for basic stats
        foreach (AudioFile audioFile in session.AudioFiles.Where(f => !string.IsNullOrEmpty(f.TranscriptText)))
        {
            AnalyzeTranscriptForStats(audioFile.TranscriptText!, stats, session.ParticipantsPresent);
        }

        // Determine most/least active participants
        if (stats.WordCounts.Any())
        {
            stats.MostTalkativePerson = stats.WordCounts.OrderByDescending(kvp => kvp.Value).First().Key;
            stats.QuietestPerson = stats.WordCounts.OrderBy(kvp => kvp.Value).First().Key;
        }

        if (stats.QuestionCounts.Any())
        {
            stats.MostInquisitivePerson = stats.QuestionCounts.OrderByDescending(kvp => kvp.Value).First().Key;
        }

        if (stats.InterruptionCounts.Any())
        {
            stats.BiggestInterruptor = stats.InterruptionCounts.OrderByDescending(kvp => kvp.Value).First().Key;
        }

        // Calculate totals
        stats.TotalInterruptions = stats.InterruptionCounts.Values.Sum();
        stats.TotalQuestions = stats.QuestionCounts.Values.Sum();
        stats.TotalLaughterMoments = stats.LaughterCounts.Values.Sum();
        stats.TotalCurseWords = stats.CurseWordCounts.Values.Sum();

        // Set conversation tone
        stats.ConversationTone = DetermineConversationTone(stats);
    }

    /// <summary>
    /// Analyzes a transcript to extract basic conversation statistics.
    /// </summary>
    private void AnalyzeTranscriptForStats(string transcript, SessionStats stats, List<string> participants)
    {
        if (string.IsNullOrEmpty(transcript)) return;

        // Split transcript into speaker segments
        string[] lines = transcript.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (string line in lines)
        {
            // Look for speaker pattern: "Speaker: text"
            Match speakerMatch = Regex.Match(line, @"^([^:]+):\s*(.+)$");
            if (!speakerMatch.Success) continue;

            string speaker = speakerMatch.Groups[1].Value.Trim();
            string text = speakerMatch.Groups[2].Value.Trim();

            if (string.IsNullOrEmpty(text)) continue;

            // Count words
            int wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            stats.WordCounts[speaker] = stats.WordCounts.GetValueOrDefault(speaker, 0) + wordCount;

            // Count questions
            int questionCount = text.Count(c => c == '?');
            if (questionCount > 0)
            {
                stats.QuestionCounts[speaker] = stats.QuestionCounts.GetValueOrDefault(speaker, 0) + questionCount;
            }

            // Count laughter indicators
            if (Regex.IsMatch(text, @"\b(haha|lol|lmao|laughing|chuckle)\b", RegexOptions.IgnoreCase))
            {
                stats.LaughterCounts[speaker] = stats.LaughterCounts.GetValueOrDefault(speaker, 0) + 1;
            }

            // Count basic interruption patterns (speaking over others)
            if (text.Contains("--") || text.Contains("wait") || text.Contains("hold on"))
            {
                stats.InterruptionCounts[speaker] = stats.InterruptionCounts.GetValueOrDefault(speaker, 0) + 1;
            }
        }
    }

    /// <summary>
    /// Determines the overall conversation tone based on statistics.
    /// </summary>
    private string DetermineConversationTone(SessionStats stats)
    {
        int totalLaughter = stats.TotalLaughterMoments;
        int totalInterruptions = stats.TotalInterruptions;
        int totalQuestions = stats.TotalQuestions;

        if (totalLaughter > 10 && totalInterruptions < 5)
            return "Light-hearted and fun";
        else if (totalInterruptions > 10)
            return "Heated and passionate";
        else if (totalQuestions > 15)
            return "Analytical and thoughtful";
        else if (totalLaughter > 5)
            return "Engaging with good humor";
        else
            return "Calm and focused discussion";
    }
}