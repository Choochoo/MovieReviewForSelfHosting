using System.Text.Json;
using System.Text.RegularExpressions;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services.Analysis;

/// <summary>
/// Service responsible for generating basic session statistics from movie session data and analysis results.
/// Provides simple metrics and summaries without complex analysis model dependencies.
/// </summary>
public class SimpleSessionStatsService
{
    private readonly ILogger<SimpleSessionStatsService> _logger;
    private readonly WordAnalysisService _wordAnalysisService;

    public SimpleSessionStatsService(
        ILogger<SimpleSessionStatsService> logger,
        WordAnalysisService wordAnalysisService)
    {
        _logger = logger;
        _wordAnalysisService = wordAnalysisService;
    }

    /// <summary>
    /// Generates session statistics from movie session and category results.
    /// </summary>
    public SessionStats GenerateSessionStats(MovieSession session, CategoryResults categoryResults)
    {
        _logger.LogInformation("[STATS DEBUG] GenerateSessionStats called for session {SessionId} - Session null: {SessionNull}, CategoryResults null: {CategoryNull}",
            session?.Id.ToString() ?? "null", session == null, categoryResults == null);

        if (session == null)
        {
            _logger.LogError("[STATS DEBUG] Session is null - cannot generate stats");
            throw new ArgumentNullException(nameof(session));
        }

        if (categoryResults == null)
        {
            _logger.LogError("[STATS DEBUG] CategoryResults is null - cannot generate stats");
            throw new ArgumentNullException(nameof(categoryResults));
        }

        _logger.LogDebug("Generating session stats for session {SessionId}", session.Id);

        SessionStats stats = new SessionStats
        {
            TotalDuration = CalculateTotalDuration(session),
            EnergyLevel = DetermineEnergyLevel(session, categoryResults),
            TechnicalQuality = AssessTechnicalQuality(session),
            HighlightMoments = CountHighlightMoments(categoryResults),
            BestMomentsSummary = CreateBestMomentsSummary(categoryResults)
        };

        // Generate basic conversation statistics
        PopulateConversationStats(stats, session);

        // Calculate interruptions from master_mix_with_speakers.json if available
        CalculateInterruptionsFromEnhancedTranscript(stats, session);

        _logger.LogInformation("Generated session stats: {Duration}, {EnergyLevel}, {HighlightCount} highlights, {Interruptions} interruptions",
            stats.TotalDuration, stats.EnergyLevel, stats.HighlightMoments, stats.TotalInterruptions);

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
        // Each audio file represents a single speaker's recording
        foreach (AudioFile audioFile in session.AudioFiles.Where(f => !string.IsNullOrEmpty(f.TranscriptText)))
        {
            // Determine the speaker name for this audio file
            string speakerName = GetSpeakerNameForAudioFile(audioFile, session);

            // Analyze this speaker's entire transcript
            AnalyzeTranscriptForSingleSpeaker(audioFile.TranscriptText!, stats, speakerName);
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

        if (stats.CurseWordCounts.Any())
        {
            stats.MostProfanePerson = stats.CurseWordCounts.OrderByDescending(kvp => kvp.Value).First().Key;
        }

        if (stats.PejorativeWordCounts.Any())
        {
            stats.MostPejorativePerson = stats.PejorativeWordCounts.OrderByDescending(kvp => kvp.Value).First().Key;
        }

        // Calculate totals
        stats.TotalInterruptions = stats.InterruptionCounts.Values.Sum();
        stats.TotalQuestions = stats.QuestionCounts.Values.Sum();
        stats.TotalLaughterMoments = stats.LaughterCounts.Values.Sum();
        stats.TotalCurseWords = stats.CurseWordCounts.Values.Sum();
        stats.TotalPejorativeWords = stats.PejorativeWordCounts.Values.Sum();

        // Set conversation tone
        stats.ConversationTone = DetermineConversationTone(stats);
    }

    /// <summary>
    /// Gets the speaker name for an audio file based on mic assignments and speaker number.
    /// </summary>
    private string GetSpeakerNameForAudioFile(AudioFile audioFile, MovieSession session)
    {
        // If the audio file has a speaker number and we have mic assignments, use those
        // Handle both 0-based and 1-based speaker numbers
        if (audioFile.SpeakerNumber.HasValue)
        {
            int speakerNum = audioFile.SpeakerNumber.Value;

            // Try direct lookup first (for 1-based numbering)
            if (session.MicAssignments.ContainsKey(speakerNum))
            {
                return session.MicAssignments[speakerNum];
            }

            // Try speakerNum + 1 (for 0-based numbering where Mic1 = index 0)
            if (session.MicAssignments.ContainsKey(speakerNum + 1))
            {
                return session.MicAssignments[speakerNum + 1];
            }
        }

        // Fall back to using the filename to determine speaker
        // Try to extract speaker info from filename patterns like "speaker1_", "mic2_", etc.
        string fileName = audioFile.FileName.ToLowerInvariant();
        foreach (KeyValuePair<int, string> kvp in session.MicAssignments)
        {
            if (fileName.Contains(kvp.Value.ToLowerInvariant()) ||
                fileName.Contains($"speaker{kvp.Key}") ||
                fileName.Contains($"mic{kvp.Key}"))
            {
                return kvp.Value;
            }
        }

        // If we still can't determine, try to use the first available mic assignment
        if (session.MicAssignments.Any())
        {
            KeyValuePair<int, string> firstMic = session.MicAssignments.OrderBy(kvp => kvp.Key).First();
            _logger.LogWarning("Could not determine speaker for audio file {FileName} with SpeakerNumber {SpeakerNumber}, using first available mic assignment: {MicName}",
                audioFile.FileName, audioFile.SpeakerNumber, firstMic.Value);
            return firstMic.Value;
        }

        // Last resort - use a generic name
        return $"Unknown Speaker";
    }

    /// <summary>
    /// Analyzes a single speaker's transcript to extract conversation statistics.
    /// </summary>
    private void AnalyzeTranscriptForSingleSpeaker(string transcript, SessionStats stats, string speakerName)
    {
        if (string.IsNullOrEmpty(transcript)) return;

        // Count total words in the transcript
        string[] words = transcript.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        int wordCount = words.Length;
        stats.WordCounts[speakerName] = stats.WordCounts.GetValueOrDefault(speakerName, 0) + wordCount;

        // Count questions (sentences ending with ?)
        int questionCount = transcript.Count(c => c == '?');
        if (questionCount > 0)
        {
            stats.QuestionCounts[speakerName] = stats.QuestionCounts.GetValueOrDefault(speakerName, 0) + questionCount;
        }

        // Count laughter indicators
        MatchCollection laughterMatches = Regex.Matches(transcript, @"\b(haha|hahaha|lol|lmao|laughing|chuckle|giggle)\b", RegexOptions.IgnoreCase);
        if (laughterMatches.Count > 0)
        {
            stats.LaughterCounts[speakerName] = stats.LaughterCounts.GetValueOrDefault(speakerName, 0) + laughterMatches.Count;
        }

        // Count curse words using shared WordAnalysisService
        AnalyzeCurseWords(transcript, stats, speakerName);

        // Count pejorative words using shared WordAnalysisService
        AnalyzePejorativeWords(transcript, stats, speakerName);

        // Note: Interruptions are better calculated from the enhanced transcript with timing data
        // This basic version can't accurately detect interruptions from plain text
    }

    /// <summary>
    /// Analyzes curse words using shared WordAnalysisService.
    /// </summary>
    private void AnalyzeCurseWords(string transcript, SessionStats stats, string speakerName)
    {
        // Enhanced curse word detection with severity tracking
        (MatchCollection mildProfanityMatches, MatchCollection strongProfanityMatches) = _wordAnalysisService.FindAllProfanity(transcript);

        int totalCurseWords = mildProfanityMatches.Count + strongProfanityMatches.Count;
        if (totalCurseWords > 0)
        {
            stats.CurseWordCounts[speakerName] = stats.CurseWordCounts.GetValueOrDefault(speakerName, 0) + totalCurseWords;

            // Track actual curse words for detailed view
            foreach (Match match in mildProfanityMatches)
            {
                string word = match.Value.ToLower();
                DetailedWordUsage? existingUsage = stats.DetailedCurseWords.FirstOrDefault(d => d.Word.Equals(word, StringComparison.OrdinalIgnoreCase) && d.Speaker == speakerName);
                if (existingUsage == null)
                {
                    existingUsage = new DetailedWordUsage
                    {
                        Word = word,
                        Speaker = speakerName,
                        Count = 0,
                        ContextExamples = new List<string>()
                    };
                    stats.DetailedCurseWords.Add(existingUsage);
                }

                existingUsage.Count++;

                string context = ExtractContextAroundMatch(transcript, match);
                if (!existingUsage.ContextExamples.Contains(context) && existingUsage.ContextExamples.Count < 3)
                {
                    existingUsage.ContextExamples.Add(context);
                }
            }

            foreach (Match match in strongProfanityMatches)
            {
                string word = match.Value.ToLower();
                DetailedWordUsage? existingUsage = stats.DetailedCurseWords.FirstOrDefault(d => d.Word.Equals(word, StringComparison.OrdinalIgnoreCase) && d.Speaker == speakerName);
                if (existingUsage == null)
                {
                    existingUsage = new DetailedWordUsage
                    {
                        Word = word,
                        Speaker = speakerName,
                        Count = 0,
                        ContextExamples = new List<string>()
                    };
                    stats.DetailedCurseWords.Add(existingUsage);
                }

                existingUsage.Count++;

                string context = ExtractContextAroundMatch(transcript, match);
                if (!existingUsage.ContextExamples.Contains(context) && existingUsage.ContextExamples.Count < 3)
                {
                    existingUsage.ContextExamples.Add(context);
                }
            }
        }
    }

    /// <summary>
    /// Analyzes pejorative words using shared WordAnalysisService.
    /// </summary>
    private void AnalyzePejorativeWords(string transcript, SessionStats stats, string speakerName)
    {
        // Pejorative and derogatory term detection
        MatchCollection pejorativeMatches = _wordAnalysisService.FindPejoratives(transcript);
        if (pejorativeMatches.Count > 0)
        {
            stats.PejorativeWordCounts[speakerName] = stats.PejorativeWordCounts.GetValueOrDefault(speakerName, 0) + pejorativeMatches.Count;

            // Track actual pejorative words for detailed view
            foreach (Match match in pejorativeMatches)
            {
                string word = match.Value.ToLower();
                DetailedWordUsage? existingUsage = stats.DetailedPejorativeWords.FirstOrDefault(d => d.Word.Equals(word, StringComparison.OrdinalIgnoreCase) && d.Speaker == speakerName);
                if (existingUsage == null)
                {
                    existingUsage = new DetailedWordUsage
                    {
                        Word = word,
                        Speaker = speakerName,
                        Count = 0,
                        ContextExamples = new List<string>()
                    };
                    stats.DetailedPejorativeWords.Add(existingUsage);
                }

                existingUsage.Count++;

                string context = ExtractContextAroundMatch(transcript, match);
                if (!existingUsage.ContextExamples.Contains(context) && existingUsage.ContextExamples.Count < 3)
                {
                    existingUsage.ContextExamples.Add(context);
                }
            }
        }
    }

    /// <summary>
    /// Extracts context around a regex match to provide usage examples.
    /// </summary>
    private string ExtractContextAroundMatch(string transcript, Match match)
    {
        int contextLength = 40; // Characters before and after the match
        int start = Math.Max(0, match.Index - contextLength);
        int end = Math.Min(transcript.Length, match.Index + match.Length + contextLength);

        string context = transcript.Substring(start, end - start).Trim();

        // Clean up the context - remove extra whitespace and line breaks
        context = Regex.Replace(context, @"\s+", " ");

        // Add ellipsis if we truncated
        if (start > 0) context = "..." + context;
        if (end < transcript.Length) context = context + "...";

        return context;
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

    /// <summary>
    /// Calculates interruptions from the enhanced transcript (master_mix_with_speakers.json).
    /// An interruption is detected when a speaker starts talking within a short overlap window
    /// while another speaker is still speaking.
    /// </summary>
    private void CalculateInterruptionsFromEnhancedTranscript(SessionStats stats, MovieSession session)
    {
        try
        {
            // Find the master_mix_with_speakers.json file
            if (session.AudioFiles == null || !session.AudioFiles.Any()) return;

            string sessionPath = Path.GetDirectoryName(session.AudioFiles.First().FilePath) ?? "";
            string enhancedTranscriptPath = Path.Combine(sessionPath, "master_mix_with_speakers.json");

            if (!File.Exists(enhancedTranscriptPath))
            {
                _logger.LogDebug("Enhanced transcript not found at {Path}, skipping interruption calculation", enhancedTranscriptPath);
                return;
            }

            // Read and parse the enhanced transcript
            string jsonContent = File.ReadAllText(enhancedTranscriptPath);
            EnhancedTranscriptionResponse? enhancedTranscript = JsonSerializer.Deserialize<EnhancedTranscriptionResponse>(jsonContent);

            if (enhancedTranscript?.result?.transcription?.utterances == null || !enhancedTranscript.result.transcription.utterances.Any())
            {
                _logger.LogDebug("No utterances found in enhanced transcript");
                return;
            }

            List<EnhancedUtterance> utterances = enhancedTranscript.result.transcription.utterances;

            // Reset interruption counts
            stats.InterruptionCounts.Clear();
            stats.TotalInterruptions = 0;

            // Analyze utterances for interruptions
            for (int i = 1; i < utterances.Count; i++)
            {
                EnhancedUtterance currentUtterance = utterances[i];
                EnhancedUtterance previousUtterance = utterances[i - 1];

                // Skip if same speaker continues talking
                if (currentUtterance.speaker == previousUtterance.speaker) continue;

                // Check for interruption: current speaker starts before previous speaker finishes
                // Using a small tolerance window (0.5 seconds) to avoid false positives from natural pauses
                double overlapThreshold = 0.5;

                if (currentUtterance.start < previousUtterance.end - overlapThreshold)
                {
                    // This is an interruption
                    string interruptor = currentUtterance.speaker;
                    stats.InterruptionCounts[interruptor] = stats.InterruptionCounts.GetValueOrDefault(interruptor, 0) + 1;
                    stats.TotalInterruptions++;

                    _logger.LogDebug("Interruption detected: {Interruptor} interrupted {Interrupted} at {Time}s",
                        interruptor, previousUtterance.speaker, currentUtterance.start);
                }
            }

            // Update most interruptor if we have data
            if (stats.InterruptionCounts.Any())
            {
                stats.BiggestInterruptor = stats.InterruptionCounts.OrderByDescending(kvp => kvp.Value).First().Key;
            }

            _logger.LogInformation("Calculated {InterruptionCount} interruptions from enhanced transcript", stats.TotalInterruptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate interruptions from enhanced transcript");
            // Don't throw - this is an enhancement, not critical functionality
        }
    }

    /// <summary>
    /// Enhanced transcript response model for deserialization.
    /// </summary>
    private class EnhancedTranscriptionResponse
    {
        public EnhancedTranscriptionResult? result { get; set; }
    }

    private class EnhancedTranscriptionResult
    {
        public EnhancedTranscriptionData? transcription { get; set; }
    }

    private class EnhancedTranscriptionData
    {
        public List<EnhancedUtterance>? utterances { get; set; }
    }

    private class EnhancedUtterance
    {
        public double start { get; set; }
        public double end { get; set; }
        public string text { get; set; } = string.Empty;
        public string speaker { get; set; } = "Unknown";
        public int speaker_number { get; set; }
    }
}
